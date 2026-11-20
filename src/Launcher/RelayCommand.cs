using System.Windows.Input;

namespace Launcher;

/// <summary>A command that runs an action. Async work goes through view-model methods that handle their own errors.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
