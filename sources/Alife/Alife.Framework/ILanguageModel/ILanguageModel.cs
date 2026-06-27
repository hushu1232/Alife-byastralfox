using Microsoft.SemanticKernel;

namespace Alife.Framework;

public interface ILanguageModel
{
    void RegisterChatCompletion(IKernelBuilder kernelBuilder);
    PromptExecutionSettings ProvidePromptExecutionSettings();
}
