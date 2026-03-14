using Bloxstrap.UI.Utility;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Shell;

namespace Bloxstrap.UI.Elements.Bootstrapper.Base
{
    public class WinFormsDialogBase : Form, IBootstrapperDialog
    {
        public const int TaskbarProgressMaximum = 100;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // added these to stop errors 
        public Bloxstrap.Bootstrapper? Bootstrapper { get; set; }

        private bool _isClosing;

        #region UI Elements
        protected virtual string _message { get; set; } = "Please wait...";
        protected virtual ProgressBarStyle _progressStyle { get; set; }
        protected virtual int _progressValue { get; set; }
        protected virtual int _progressMaximum { get; set; }
        protected virtual TaskbarItemProgressState _taskbarProgressState { get; set; }
        protected virtual double _taskbarProgressValue { get; set; }
        protected virtual bool _cancelEnabled { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Message
        {
            get => _message;
            set
            {
                if (InvokeRequired)
                    Invoke(() => _message = value);
                else
                    _message = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ProgressBarStyle ProgressStyle
        {
            get => _progressStyle;
            set
            {
                if (InvokeRequired)
                    Invoke(() => _progressStyle = value);
                else
                    _progressStyle = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                if (InvokeRequired)
                    Invoke(() => _progressMaximum = value);
                else
                    _progressMaximum = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (InvokeRequired)
                    Invoke(() => _progressValue = value);
                else
                    _progressValue = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TaskbarItemProgressState TaskbarProgressState
        {
            get => _taskbarProgressState;
            set
            {
                _taskbarProgressState = value;
                TaskbarProgress.SetProgressState(Process.GetCurrentProcess().MainWindowHandle, value);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double TaskbarProgressValue
        {
            get => _taskbarProgressValue;
            set
            {
                _taskbarProgressValue = value;
                TaskbarProgress.SetProgressValue(Process.GetCurrentProcess().MainWindowHandle, (int)value, TaskbarProgressMaximum);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CancelEnabled
        {
            get => _cancelEnabled;
            set
            {
                if (InvokeRequired)
                    Invoke(() => _cancelEnabled = value);
                else
                    _cancelEnabled = value;
            }
        }
        #endregion

        public void ScaleWindow()
        {
            Size = MinimumSize = MaximumSize = WindowScaling.GetScaledSize(Size);

            foreach (Control control in Controls)
            {
                control.Size = WindowScaling.GetScaledSize(control.Size);
                control.Location = WindowScaling.GetScaledPoint(control.Location);
                control.Padding = WindowScaling.GetScaledPadding(control.Padding);
            }
        }

        public void SetupDialog()
        {
            Text = App.Settings.Prop.BootstrapperTitle;
            Icon = App.Settings.Prop.BootstrapperIcon.GetIcon();

            if (Locale.RightToLeft)
            {
                this.RightToLeft = RightToLeft.Yes;
                this.RightToLeftLayout = true;
            }
        }

        #region WinForms event handlers
        public void ButtonCancel_Click(object? sender, EventArgs e) => Close();

        public void Dialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isClosing)
                Bootstrapper?.Cancel();
        }
        #endregion

        #region IBootstrapperDialog Methods
        public void ShowBootstrapper() => ShowDialog();

        public virtual void CloseBootstrapper()
        {
            if (InvokeRequired)
            {
                Invoke(CloseBootstrapper);
            }
            else
            {
                _isClosing = true;
                Close();
            }
        }

        public virtual void ShowSuccess(string message, Action? callback) => BaseFunctions.ShowSuccess(message, callback);
        #endregion
    }
}
