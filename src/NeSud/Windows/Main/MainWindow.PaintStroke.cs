using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    // Capture the board, not an individual Button. Queue sampled cells even while a server move is in flight.
    private void AttachPaintStroke(Grid board, Func<int, bool, Task> apply, Func<bool> canStart)
    {
        var pending = new Queue<(int Cell, bool First)>();
        var visited = new HashSet<int>();
        bool held = false, draining = false;
        Point? previous = null;

        async Task Drain()
        {
            if (draining) return;
            draining = true;
            try
            {
                while (pending.Count > 0)
                {
                    if (!board.IsVisible || !state.PaintDigit) { pending.Clear(); break; }
                    var item = pending.Dequeue();
                    await apply(item.Cell, item.First);
                    // Let WPF dispatch pending keyboard/mouse events between cell updates.
                    if (pending.Count > 0) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            catch (Exception ex)
            {
                pending.Clear();
                Finish(true);
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally { draining = false; }
        }

        int CellAt(Point p)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= board.ActualWidth || p.Y >= board.ActualHeight) return -1;
            return Math.Min(8, (int)(p.Y * 9 / board.ActualHeight)) * 9 +
                Math.Min(8, (int)(p.X * 9 / board.ActualWidth));
        }

        void Visit(Point p)
        {
            int i = CellAt(p);
            if (i >= 0 && visited.Add(i)) pending.Enqueue((i, visited.Count == 1));
        }

        void Sample(Point p)
        {
            if (previous is { } last)
            {
                double step = Math.Max(1, Math.Min(board.ActualWidth, board.ActualHeight) / 36);
                int n = Math.Max(1, (int)Math.Ceiling((p - last).Length / step));
                for (int k = 1; k <= n; k++) Visit(last + (p - last) * (k / (double)n));
            }
            else Visit(p);
            previous = p;
            _ = Drain();
        }

        void Finish(bool cancel)
        {
            held = false;
            previous = null;
            if (cancel) pending.Clear();
            if (Mouse.Captured == board) board.ReleaseMouseCapture();
        }

        board.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!state.PaintDigit) return;
            e.Handled = true;
            if (!canStart()) return;
            if (held) Finish(false);
            visited.Clear();
            previous = null;
            if (!board.CaptureMouse()) return;
            held = true;
            Sample(e.GetPosition(board));
        };
        board.PreviewMouseMove += (_, e) =>
        {
            if (!held) return;
            if (e.LeftButton != MouseButtonState.Pressed) { Finish(false); return; }
            e.Handled = true;
            Sample(e.GetPosition(board));
        };
        board.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!held) return;
            e.Handled = true;
            Sample(e.GetPosition(board));
            Finish(false);
        };
        board.LostMouseCapture += (_, _) => { if (held) Finish(true); };
        board.Unloaded += (_, _) => Finish(true);
    }

    private Guid localStrokeGame;
    private int localStrokeDigit;
    private bool localStrokeNote;

    private void PaintLocalStroke(int i, bool first)
    {
        if (!Playing || G is not { } g) return;
        if (first)
        {
            localStrokeGame = state.GameId;
            localStrokeDigit = g.Values[i] != 0 ? g.Values[i] : ActivePaintDigit;
            localStrokeNote = g.NotesMode || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            ClickCell(i);
            return;
        }
        int digit = localStrokeDigit;
        if (localStrokeGame != state.GameId || digit == 0 || g.Remaining(digit) == 0 ||
            g.Values[i] != 0 || !g.Editable(i)) return;
        PaintLocalCell(i, digit, localStrokeNote);
    }
}
