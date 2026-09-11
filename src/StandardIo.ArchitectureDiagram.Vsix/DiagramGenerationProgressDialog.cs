using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace StandardIo.ArchitectureDiagram.Vsix;

internal sealed class DiagramGenerationProgressDialog : Form
{
    private readonly CancellationTokenSource _cancellation;
    private readonly Label _stage;
    private bool _completed;

    public DiagramGenerationProgressDialog(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        Text = "Generating architecture diagram";
        ClientSize = new Size(460, 125);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        _stage = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(428, 28),
            Text = "Preparing generation..."
        };
        var progress = new ProgressBar
        {
            Location = new Point(16, 50),
            Size = new Size(428, 18),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25
        };
        var cancel = new Button
        {
            DialogResult = DialogResult.None,
            Location = new Point(369, 82),
            Size = new Size(75, 27),
            Text = "Cancel"
        };
        cancel.Click += (_, _) =>
        {
            cancel.Enabled = false;
            _stage.Text = "Cancelling...";
            _cancellation.Cancel();
        };

        Controls.Add(_stage);
        Controls.Add(progress);
        Controls.Add(cancel);
        CancelButton = cancel;
    }

    public void SetStage(string stage)
    {
        if (!IsDisposed)
        {
            _stage.Text = stage;
        }
    }

    public void Complete()
    {
        _completed = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_completed)
        {
            _cancellation.Cancel();
        }

        base.OnFormClosing(e);
    }
}
