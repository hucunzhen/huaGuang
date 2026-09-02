using Android.Text;
using Android.Views.InputMethods;
using AndroidX.AppCompat.Widget;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Android;

public sealed class AndroidScannerInputMethodGuard : IScannerInputMethodGuard
{
    public IDisposable EnterEnglishInputMode(Entry? entry = null)
    {
        if (entry?.Handler?.PlatformView is not AppCompatEditText editText)
        {
            return NoOpScannerInputMethodGuard.EmptyScope.Instance;
        }

        var previousInputType = editText.InputType;
        editText.InputType = InputTypes.ClassText |
                             InputTypes.TextFlagNoSuggestions |
                             InputTypes.TextVariationVisiblePassword;
        editText.ImeOptions = ImeAction.Done;

        return new RestoreScope(editText, previousInputType);
    }

    sealed class RestoreScope : IDisposable
    {
        readonly AppCompatEditText _editText;
        readonly InputTypes _previousInputType;
        bool _disposed;

        public RestoreScope(AppCompatEditText editText, InputTypes previousInputType)
        {
            _editText = editText;
            _previousInputType = previousInputType;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _editText.InputType = _previousInputType;
        }
    }
}
