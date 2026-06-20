using System;
using System.IO;
using System.Text;
using System.Windows;

using Alife.Function.DeskPet;

namespace Alife.DeskPet;

public partial class App
{
    PetActivity activity = null!;

    protected override async void OnStartup(StartupEventArgs startupEvent)
    {
        try
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            File.Create("pet.log").Close();

            base.OnStartup(startupEvent);
            await File.AppendAllTextAsync("pet.log", "[startup] app startup" + Environment.NewLine);

            string[] args = Environment.GetCommandLineArgs();
            string defaultModel = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot/model/Mao/Mao.model3.json");
            string modelPath = args.Length > 1 ? args[1] : defaultModel;
            await File.AppendAllTextAsync("pet.log", "[startup] modelPath=" + modelPath + Environment.NewLine);

            PetModelMetadata metadata = PetModelMetadata.Load(modelPath);
            await File.AppendAllTextAsync("pet.log", "[startup] metadata loaded" + Environment.NewLine);

            MainWindow mainWindow = await Alife.DeskPet.MainWindow.Create();
            await File.AppendAllTextAsync("pet.log", "[startup] main window created" + Environment.NewLine);

            PetProcess petProcess = new(Console.Out, Console.In);
            PetBridge bridge = new(mainWindow.WebView, metadata);
            await File.AppendAllTextAsync("pet.log", "[startup] bridge created" + Environment.NewLine);

            MainWindow = mainWindow;
            activity = new(petProcess, bridge, metadata, mainWindow);
            await File.AppendAllTextAsync("pet.log", "[startup] activity created" + Environment.NewLine);

            mainWindow.NavigateRenderer();
            await File.AppendAllTextAsync("pet.log", "[startup] renderer navigation requested" + Environment.NewLine);
        }
        catch (Exception e)
        {
            await File.AppendAllTextAsync("pet.log", e + Environment.NewLine);
        }
    }
}
