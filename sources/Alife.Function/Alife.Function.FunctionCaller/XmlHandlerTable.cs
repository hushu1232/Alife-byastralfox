using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Interpreter;

public class XmlHandlerTable
{
    public IReadOnlyList<XmlHandler> Handlers => xmlHandlers;
    public XmlFunctionExecutionPolicy ExecutionPolicy { get; } = new();

    public void Register(XmlHandler handler)
    {
        xmlHandlers.Add(handler);
        foreach (XmlFunction xmlFunction in handler.Functions)
        {
            if (xmlFunctions.TryGetValue(xmlFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup) == false)
            {
                xmlFunctionGroup = new SortedSet<XmlFunction>();
                xmlFunctions[xmlFunction.Name] = xmlFunctionGroup;
            }

            xmlFunctionGroup.Add(xmlFunction);
        }
    }

    public void Unregister(XmlHandler handler)
    {
        xmlHandlers.Remove(handler);
        foreach (XmlFunction xmlHandlerFunction in handler.Functions)
        {
            if (xmlFunctions.TryGetValue(xmlHandlerFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup))
                xmlFunctionGroup.Remove(xmlHandlerFunction);
        }
    }

    public bool ContainsFunction(string name)
    {
        return xmlFunctions.TryGetValue(name.ToLower(), out SortedSet<XmlFunction>? xmlFunctionGroup)
               && xmlFunctionGroup.Count > 0;
    }

    public string Document(Func<XmlFunction, bool>? functionFilter = null)
    {
        StringBuilder sb = new();
        foreach (XmlHandler handler in xmlHandlers)
        {
            if (handler.IsImplicit)
                continue;

            string document = handler.Document(functionFilter);
            if (string.IsNullOrWhiteSpace(document))
                continue;

            sb.AppendLine(document);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public async Task Handle(string name, XmlContext tagContext, CancellationToken cancellationToken = default)
    {
        SortedSet<XmlFunction>? xmlFunctionGroup = xmlFunctions.GetValueOrDefault(name.ToLower());
        if (xmlFunctionGroup == null || xmlFunctionGroup.Count == 0)
            throw new Exception($"未找到名为 {name} 的可调用函数");

        ToolRouteDecision? route = ExecutionPolicy.CurrentRoute;
        if (route is not null && route.Allows(name) && route.BoundParameters.Count > 0)
        {
            Dictionary<string, string> parameters = tagContext.Parameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in route.BoundParameters)
                parameters[key] = value;
            tagContext = new XmlContext
            {
                CallMode = tagContext.CallMode,
                Content = tagContext.Content,
                Parameters = parameters
            };
        }

        foreach (XmlFunction xmlFunction in xmlFunctionGroup)
        {
            XmlFunctionExecutionDecision decision = ExecutionPolicy.TryConsume(xmlFunction, tagContext);
            if (decision.IsAllowed == false)
                throw new InvalidOperationException(decision.Reason);

            await xmlFunction.Invoker(tagContext, cancellationToken);
        }
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, SortedSet<XmlFunction>> xmlFunctions = new();
}
