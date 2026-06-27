using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Framework;

public struct AwakeContext
{
    public Character Character { get; init; }
    public IServiceProvider Services { get; init; }
    public IKernelBuilder KernelBuilder { get; init; }
    public ChatHistoryAgentThread ContextBuilder { get; init; }
}

public interface ISystemEvent
{

    /// <summary>
    /// 活动初始化时调用，此时AI还未激活，你有机会去调整它的插件环境和上下文内容
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task AwakeAsync(AwakeContext context) => Task.CompletedTask;
    /// <summary>
    /// AI被激活后触发
    /// </summary>
    /// <param name="kernel"></param>
    /// <param name="chatActivity"></param>
    /// <returns></returns>
    public Task StartAsync(Kernel kernel, ChatActivity chatActivity) => Task.CompletedTask;
    /// <summary>
    /// AI即将关闭时触发
    /// </summary>
    /// <returns></returns>
    public Task DestroyAsync() => Task.CompletedTask;
}
