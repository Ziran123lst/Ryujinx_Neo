using Ryujinx.HLE;
using Ryujinx.HLE.HOS.Applets;
using Ryujinx.HLE.HOS.Applets.SoftwareKeyboard;
using Ryujinx.HLE.HOS.Services.Am.AppletOE.ApplicationProxyService.ApplicationProxy.Types;
using Ryujinx.HLE.UI;
using System;
using System.Threading;

namespace LibKenjinx.Android
{
    internal class AndroidUIHandler : IHostUIHandler, IDisposable
    {
        private bool _isDisposed;
        private bool _isOkPressed;
        private string? _input;
        private ManualResetEvent _resetEvent = new(false);

        public IHostUITheme HostUITheme => throw new NotImplementedException();

        public IDynamicTextInputHandler CreateDynamicTextInputHandler()
        {
            throw new NotImplementedException();
        }

        public void TakeScreenshot()
        {
            throw new NotImplementedException();
        }

        public bool DisplayErrorAppletDialog(string? title, string? message, string[] buttonsText)
        {
            Interop.UpdateUiHandler(title ?? "",
                message ?? "",
                "",
                1,
                0,
                0,
                KeyboardMode.Default,
                "",
                "");

            return _isOkPressed;
        }

        public bool DisplayInputDialog(SoftwareKeyboardUIArgs args, out string userText)
        {
            _input = null;
            _resetEvent.Reset();
            Interop.UpdateUiHandler("软件键盘",
                args.HeaderText ?? "",
                args.GuideText ?? "",
                2,
                args.StringLengthMin,
                args.StringLengthMax,
                args.KeyboardMode,
                args.SubtitleText ?? "",
                args.InitialText ?? "");

            _resetEvent.WaitOne();

            userText = _input ?? "";

            return _isOkPressed;
        }

        public bool DisplayMessageDialog(string? title, string? message)
        {
            Interop.UpdateUiHandler(title ?? "",
                message ?? "",
                "",
                1,
                0,
                0,
                KeyboardMode.Default,
                "",
                "");

            return _isOkPressed;
        }

        public bool DisplayMessageDialog(ControllerAppletUIArgs args)
        {
            string playerCount = args.PlayerCountMin == args.PlayerCountMax ? $"正好 {args.PlayerCountMin} 个" : $"{args.PlayerCountMin}-{args.PlayerCountMax}";

            string message = $"应用程序请求 **{playerCount}** 名玩家，要求如下：\n\n"
                           + $"**类型：** {args.SupportedStyles}\n\n"
                           + $"**玩家：** {string.Join(", ", args.SupportedPlayers)}\n\n"
                           + (args.IsDocked ? "已设置底座模式。`掌机模式`同样无效。\n\n" : "")
                           + "_请重新配置输入设置，然后按确定。_";

            return DisplayMessageDialog("手柄 Applet", message);
        }

        public bool DisplayCabinetDialog(out string userText)
		{
			userText="";
			return false;
		}

        public void DisplayCabinetMessageDialog()
		{
			
		}

        public void ExecuteProgram(Switch device, ProgramSpecifyKind kind, ulong value)
        {
           // throw new NotImplementedException();
        }

        internal void SetResponse(bool isOkPressed, string input)
        {
            if (_isDisposed)
            {
                return;
            }

            _isOkPressed = isOkPressed;
            _input = input;
            _resetEvent.Set();
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
