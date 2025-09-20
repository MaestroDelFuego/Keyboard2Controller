using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace KeyboardToController
{
    public class MapperForm : Form
    {
        private Label statusLabel;
        private ViGEmClient _client;
        private IXbox360Controller _controller;
        private Dictionary<Keys, bool> _keyState = new Dictionary<Keys, bool>();

        // Keyboard hook
        private const int WH_KEYBOARD_LL = 13;
        private static IntPtr _hookID = IntPtr.Zero;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _proc;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        public MapperForm()
        {
            Text = "Keyboard -> Xbox Controller Mapper";
            Width = 420;
            Height = 160;

            statusLabel = new Label() { Left = 10, Top = 10, Width = 380, Height = 70 };
            Controls.Add(statusLabel);

            var btnConnect = new Button() { Left = 10, Top = 80, Width = 120, Text = "Connect pad" };
            btnConnect.Click += BtnConnect_Click;
            Controls.Add(btnConnect);

            var btnDisconnect = new Button() { Left = 140, Top = 80, Width = 120, Text = "Disconnect" };
            btnDisconnect.Click += BtnDisconnect_Click;
            Controls.Add(btnDisconnect);

            var btnQuit = new Button() { Left = 270, Top = 80, Width = 120, Text = "Quit" };
            btnQuit.Click += (s, e) => { Close(); };
            Controls.Add(btnQuit);

            UpdateStatus("Install ViGEmBus → Click Connect → Start game");

            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            DisconnectController();
            base.OnFormClosing(e);
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectController();
                UpdateStatus("Controller connected! WASD=Left Stick, numpad 4,5,8,6=Right Stick, Arrows=DPad, Space=A, Shift=B, Q=LT, E=RT");
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed: " + ex.Message);
            }
        }

        private void BtnDisconnect_Click(object sender, EventArgs e)
        {
            DisconnectController();
            UpdateStatus("Disconnected.");
        }

        private void UpdateStatus(string text)
        {
            statusLabel.Text = text;
        }

        private void ConnectController()
        {
            if (_client != null) return;
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller(); // <-- public factory method
            _controller.Connect();
        }

        private void DisconnectController()
        {
            if (_controller != null)
            {
                try { _controller.Disconnect(); } catch { }
                _controller = null;
            }
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (_controller != null))
            {
                int wm = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                if (wm == WM_KEYDOWN || wm == WM_SYSKEYDOWN)
                {
                    if (!_keyState.ContainsKey(key) || !_keyState[key])
                    {
                        _keyState[key] = true;
                        HandleKeyChange(key, true);
                    }
                }
                else if (wm == WM_KEYUP || wm == WM_SYSKEYUP)
                {
                    _keyState[key] = false;
                    HandleKeyChange(key, false);
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void HandleKeyChange(Keys key, bool pressed)
        {
            // --- Buttons ---
            if (key == Keys.Space) { _controller.SetButtonState(Xbox360Button.A, pressed); return; }
            if (key == Keys.LShiftKey || key == Keys.RShiftKey) { _controller.SetButtonState(Xbox360Button.B, pressed); return; }
            if (key == Keys.Enter) { _controller.SetButtonState(Xbox360Button.Start, pressed); return; }

            // --- Triggers ---
            if (key == Keys.Q) { _controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(pressed ? 255 : 0)); return; }
            if (key == Keys.E) { _controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(pressed ? 255 : 0)); return; }

            // --- DPad ---
            if (key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right) { UpdateDPad(); return; }

            // --- Left Stick (WASD) ---
            if (key == Keys.W || key == Keys.A || key == Keys.S || key == Keys.D) { UpdateLeftStick(); return; }

            // --- Right Stick (IJKL) ---
            if (key == Keys.NumPad8 || key == Keys.NumPad4 || key == Keys.NumPad5 || key == Keys.NumPad6) { UpdateRightStick(); return; }

        }

        private void UpdateDPad()
        {
            bool up = _keyState.ContainsKey(Keys.Up) && _keyState[Keys.Up];
            bool down = _keyState.ContainsKey(Keys.Down) && _keyState[Keys.Down];
            bool left = _keyState.ContainsKey(Keys.Left) && _keyState[Keys.Left];
            bool right = _keyState.ContainsKey(Keys.Right) && _keyState[Keys.Right];

            _controller.SetButtonState(Xbox360Button.Up, up);
            _controller.SetButtonState(Xbox360Button.Down, down);
            _controller.SetButtonState(Xbox360Button.Left, left);
            _controller.SetButtonState(Xbox360Button.Right, right);
        }

        private void UpdateLeftStick()
        {
            short x = 0, y = 0;
            if (_keyState.ContainsKey(Keys.A) && _keyState[Keys.A]) x -= 32767;
            if (_keyState.ContainsKey(Keys.D) && _keyState[Keys.D]) x += 32767;
            if (_keyState.ContainsKey(Keys.W) && _keyState[Keys.W]) y += 32767;
            if (_keyState.ContainsKey(Keys.S) && _keyState[Keys.S]) y -= 32767;

            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, x);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, y);
        }

        private void UpdateRightStick()
        {
            short x = 0, y = 0;
            if (_keyState.ContainsKey(Keys.NumPad4) && _keyState[Keys.NumPad4]) x -= 32767;
            if (_keyState.ContainsKey(Keys.NumPad6) && _keyState[Keys.NumPad6]) x += 32767;
            if (_keyState.ContainsKey(Keys.NumPad8) && _keyState[Keys.NumPad8]) y += 32767;
            if (_keyState.ContainsKey(Keys.NumPad5) && _keyState[Keys.NumPad5]) y -= 32767;

            _controller.SetAxisValue(Xbox360Axis.RightThumbX, x);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, y);
        }
    }
}
