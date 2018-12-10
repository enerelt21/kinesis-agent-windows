/*
 * Copyright 2018 Amazon.com, Inc. or its affiliates. All Rights Reserved.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License").
 * You may not use this file except in compliance with the License.
 * A copy of the License is located at
 * 
 *  http://aws.amazon.com/apache2.0
 * 
 * or in the "license" file accompanying this file. This file is distributed
 * on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
 * express or implied. See the License for the specific language governing
 * permissions and limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.Logging;

using Amazon.KinesisTap.Expression.Ast;

namespace Amazon.KinesisTap.Expression.ObjectDecoration
{
    /// <summary>
    /// Evaluator for object decoration. Only need to NodeList and dependent nodes
    /// </summary>
    public class ObjectDecorationInterpreter<TData> : IObjectDecorationAstVisitor<TData, object>
    {
        protected readonly IObjectDecorationEvaluationContext<TData> _evaludationContext;

        public ObjectDecorationInterpreter(IObjectDecorationEvaluationContext<TData> evaludationContext)
        {
            _evaludationContext = evaludationContext;
        }

        public virtual object Visit(Node node, TData data)
        {
            IdentifierNode identifierNode = node as IdentifierNode;
            if (identifierNode != null) return VisitIdentifier(identifierNode, data);

            InvocationNode invocationNode = node as InvocationNode;
            if (invocationNode != null) return VisitInvocationNode(invocationNode, data);

            NodeList<Node> nodeList = node as NodeList<Node>;
            if (nodeList != null) return VisitNodeList(nodeList, data);

            NodeList<KeyValuePairNode> keyValuePairNodes = node as NodeList<KeyValuePairNode>;
            if (keyValuePairNodes != null) return VisitObjectDecoration(keyValuePairNodes, data);

            LiteralNode literalNode = node as LiteralNode;
            if (literalNode != null) return VisitLiteral(literalNode, data);

            KeyValuePairNode keyValuePairNode = node as KeyValuePairNode;
            if (keyValuePairNode != null) return VisitKeyValuePairNode(keyValuePairNode, data);


            throw new NotImplementedException();
        }

        public virtual object VisitIdentifier(IdentifierNode identifierNode, TData data)
        {
            var variableName = identifierNode.Identifier;
            if (IsLocal(variableName))
            {
                return _evaludationContext.GetLocalVariable(variableName, data);
            }
            else
            {
                return _evaludationContext.GetVariable(variableName);
            }
        }

        public virtual object VisitInvocationNode(InvocationNode invocationNode, TData data)
        {
            string functionName = invocationNode.FunctionName.Identifier;
            int argumentCount = invocationNode.Arguments.Count;
            object[] arguments = new object[argumentCount];
            Type[] argumentTypes = new Type[argumentCount];
            bool hasNullArguments = false;
            for(int i = 0; i < argumentCount; i++)
            {
                arguments[i] = Visit(invocationNode.Arguments[i], data);
                if (arguments[i] == null)
                {
                    hasNullArguments = true;
                    argumentTypes[i] = typeof(object);
                }
                else
                {
                    argumentTypes[i] = arguments[i].GetType();
                }
            }
            MethodInfo methodInfo = _evaludationContext.FunctionBinder.Resolve(functionName, argumentTypes);
            if (methodInfo == null)
            {
                //If we have null arguments, we will propagate null without warning.
                if (!hasNullArguments)
                {
                    _evaludationContext.Logger?.LogWarning($"Cannot resolve function {functionName} with argument types {string.Join(",", argumentTypes.Select(t => t.Name))}");
                }
                return null;
            }
            else
            {
                return methodInfo.Invoke(null, arguments);
            }
        }

        public virtual object VisitKeyValuePairNode(KeyValuePairNode keyValuePairNode, TData data)
        {
            string key = keyValuePairNode.Key;
            string value = $"{VisitNodeList((NodeList<Node>)(keyValuePairNode.Value), data)}";
            return new KeyValuePair<string, string>(key, value);
        }

        public virtual object VisitLiteral(LiteralNode literalNode, TData data)
        {
            return literalNode.Value;
        }

        public virtual object VisitNodeList(NodeList<Node> nodeList, TData data)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach(var node in nodeList.List)
            {
                stringBuilder.Append(Visit(node, data));
            }
            return stringBuilder.ToString();
        }

        public virtual object VisitObjectDecoration(NodeList<KeyValuePairNode> keyValuePairNodes, TData data)
        {
            IDictionary<string, string> attributes = new Dictionary<string, string>();
            foreach(var keyValuePairNode in keyValuePairNodes.List)
            {
                var kv = (KeyValuePair<string, string>)Visit(keyValuePairNode, data);
                if (!string.IsNullOrWhiteSpace(kv.Value)) //Supress whiteplaces
                {
                    attributes.Add(kv);
                }
            }
            return attributes;
        }

        protected virtual bool IsLocal(string variableName)
        {
            return variableName.StartsWith("$")
                || variableName.StartsWith("_")
                || variableName.ToLower().Equals("timestamp");
        }
    }
}
