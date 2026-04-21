using AntDesign;
using Microsoft.AspNetCore.Components;

public class FragmentService
{
    public void ShowException(string title, Exception exception)
    {
        RenderFragment content = (builder) => {
            builder.OpenElement(0, "div");
            builder.OpenElement(1, "Text");
            builder.AddContent(2, "错误详情：" + exception.Message);
            builder.CloseElement();
            builder.OpenElement(3, "br");
            builder.CloseElement();
            builder.OpenElement(4, "pre");
            builder.AddAttribute(5, "style",
                "max-height: 300px; overflow-y: auto; margin-top: 12px; font-size: 12px; color: #ff4d4f; background: #fff1f0; padding: 12px; border: 1px solid #ffccc7; border-radius: 4px;");
            builder.AddContent(6, exception.StackTrace);
            builder.CloseElement();
            builder.CloseElement();
        };

        _ = modalService.Error(new ConfirmOptions {
            Title = title,
            Content = content,
            Width = 800,
            Centered = true
        });
    }

    public FragmentService(ModalService modalService)
    {
        this.modalService = modalService;
    }

    readonly ModalService modalService;
}
