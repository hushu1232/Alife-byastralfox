using System;
using System.Collections.Generic;

namespace Alife.Services;

public class TutorialStep
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string TargetElementSelector { get; set; } = "";
    public string? RequiredActionName { get; set; }
}

public class TutorialService
{
    public event Action? OnChanged;
    public bool IsActive 
    { 
        get => _isActive; 
        set { _isActive = value; OnChanged?.Invoke(); } 
    }
    private bool _isActive;

    public int CurrentStepIndex 
    { 
        get => _currentStepIndex; 
        set { _currentStepIndex = value; OnChanged?.Invoke(); } 
    }
    private int _currentStepIndex = 0;

    public List<TutorialStep> Steps => steps;

    private readonly List<TutorialStep> steps = new()
    {
        new TutorialStep { 
            Title = "第一步：进入系统设置", 
            Content = "点击左侧的齿轮图标进入“系统管理中心”。这里可以管理插件、角色和系统配置喵！",
            TargetElementSelector = "#nav-system",
            RequiredActionName = "EnterSystem"
        },
        new TutorialStep { 
            Title = "第二步：配置对话模型", 
            Content = "找到“LLM对话能力”，点击齿轮图标。你需要填入 API Key 和 BaseUrl 才能让 AI 动起来喵！",
            TargetElementSelector = "#plugin-config-llm",
            RequiredActionName = "ConfigLLM"
        },
        new TutorialStep { 
            Title = "第三步：创建你的角色", 
            Content = "点击“创建新角色”卡片。给你的 AI 伙伴起一个好听的名字喵！",
            TargetElementSelector = "#btn-new-character",
            RequiredActionName = "CreateCharacter"
        },
        new TutorialStep { 
            Title = "第四步：设定与插件", 
            Content = "在这里写下角色的性格特征。别忘了在右侧勾选它需要的插件，让它拥有超能力喵！",
            TargetElementSelector = "#character-plugin-tree"
        },
        new TutorialStep { 
            Title = "最后一步：激活并对话", 
            Content = "点击右上角的“激活角色”按钮。激活成功后，你就可以前往聊天窗口和它交流了喵！",
            TargetElementSelector = "#btn-activate-character",
            RequiredActionName = "ActivateCharacter"
        }
    };

    public TutorialStep? CurrentStep => IsActive && CurrentStepIndex < steps.Count ? steps[CurrentStepIndex] : null;

    public void Start()
    {
        IsActive = true;
        CurrentStepIndex = 0;
    }

    public void Next()
    {
        CurrentStepIndex++;
        if (CurrentStepIndex >= steps.Count)
        {
            Stop();
        }
    }

    public void NotifyAction(string actionName)
    {
        if (IsActive && CurrentStep?.RequiredActionName == actionName)
        {
            Next();
        }
    }

    public void Stop()
    {
        IsActive = false;
    }
}
