using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SpellCrafter
{
    public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        : ICommand
    {
        private readonly Action<object?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        : ICommand, INotifyPropertyChanged
    {
        private readonly Func<object?, Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<object?, bool>? _canExecute = canExecute;
        private bool _isExecuting;
        private Exception? _lastException;

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting == value)
                    return;

                _isExecuting = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExecuting)));
                RaiseCanExecuteChanged();
            }
        }

        public Exception? LastException
        {
            get => _lastException;
            private set
            {
                if (_lastException == value)
                    return;

                _lastException = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastException)));
            }
        }

        public event EventHandler? CanExecuteChanged;
        public event EventHandler<Exception>? ExecutionFailed;
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool CanExecute(object? parameter) =>
            !IsExecuting && (_canExecute == null || _canExecute(parameter));

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            IsExecuting = true;
            LastException = null;

            try
            {
                await _execute(parameter);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected - don't set LastException or raise ExecutionFailed
                Debug.WriteLine("Command execution was canceled.");
            }
            catch (Exception ex)
            {
                LastException = ex;
                Debug.WriteLine(ex);
                ExecutionFailed?.Invoke(this, ex);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
