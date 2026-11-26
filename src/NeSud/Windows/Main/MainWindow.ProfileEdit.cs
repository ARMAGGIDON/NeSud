using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using NeSud.Core;
using SkiaSharp;
using Svg.Skia;

namespace NeSud;

public sealed partial class MainWindow
{
    private static BitmapImage ReadAvatar(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task EditOwnProfile(bool sendVerification = false)
    {
        if (account is null) return;
        AccountSettings settings;
        try { settings = await Request<AccountSettings>("account/settings"); }
        catch (Exception ex) { Dialog("Профіль", ex.Message, false); return; }

        var panel = Stack();
        panel.Children.Add(Text("Редагування профілю", 24, "Text", true));
        panel.Children.Add(Text("Нікнейм"));
        var username = new TextBox { Text = settings.Username, MaxLength = 32 };
        panel.Children.Add(username);
        panel.Children.Add(Text("Електронна пошта"));
        var email = new TextBox { Text = settings.Email ?? "", MaxLength = 254, IsReadOnly = settings.Verified, IsEnabled = !settings.Verified };
        panel.Children.Add(email);
        var notice = Text("", 14, "Muted");
        var verification = Stack();

        bool busy = false;
        var window = Modal("Редагування профілю", panel, 620, 760);

        void Apply(AccountSettings current)
        {
            settings = current;
            username.Text = current.Username;
            username.IsReadOnly = !current.Verified;
            email.Text = current.Email ?? "";
            email.IsReadOnly = current.Verified;
            email.IsEnabled = !current.Verified;
            email.ToolTip = current.Verified ? "Пошта підтверджена. Зміна недоступна." : null;
            ToolTipService.SetShowOnDisabled(email, true);
            verification.Visibility = current.Verified ? Visibility.Collapsed : Visibility.Visible;
            if (account is not null)
            {
                if (account.Username != current.Username)
                {
                    InvalidatePlayerAvatar(account.Username ?? account.User);
                    account = account with { Username = current.Username };
                }
                RememberSession(account);
                UpdateConnection();
            }
        }

        async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            try { await action(); }
            catch (Exception ex) { if (window.IsVisible) notice.Text = ex.Message; }
            finally { busy = false; }
        }

        panel.Children.Add(Action("Зберегти", async () => await Run(async () =>
        {
            Apply(await Request<AccountSettings>("account/settings", new ProfileEdit(username.Text, email.Text)));
            notice.Text = "Збережено.";
        }), primary: true));
        Gap(panel);
        verification.Children.Add(Action("Надіслати код на пошту", async () => await Run(async () =>
        {
            await Request<SyncAck>("account/verification/send", new { });
            notice.Text = "Код надіслано на пошту.";
        })));
        var code = new TextBox { MaxLength = 16 };
        verification.Children.Add(Text("Код із листа"));
        verification.Children.Add(code);
        verification.Children.Add(Action("Підтвердити пошту", async () => await Run(async () =>
        {
            Apply(await Request<AccountSettings>("account/verification", new VerificationRequest(code.Text.Trim())));
            notice.Text = "";
            await Sync();
            window.Close();
        })));
        verification.Children.Add(Text("Зміна нікнейму, аватара та друзі доступні після підтвердження пошти.", 13, "Muted"));
        panel.Children.Add(verification);
        panel.Children.Add(notice);
        Apply(settings);
        if (sendVerification && !settings.Verified)
        {
            window.Loaded += async (_, _) => await Run(async () =>
            {
                await Request<SyncAck>("account/verification/send", new { });
                if (window.IsVisible) notice.Text = "Код надіслано на пошту.";
            });
        }
        window.ShowDialog();
    }

    private async Task EditAvatar()
    {
        var session = account;
        if (session is null) return;
        var picker = new OpenFileDialog
        {
            Title = "Вибір аватара",
            Filter = "Зображення (*.jpg;*.jpeg;*.png;*.svg)|*.jpg;*.jpeg;*.png;*.svg"
        };
        if (picker.ShowDialog(this) != true) return;
        var info = new FileInfo(picker.FileName);
        if (info.Length > 5 * 1024 * 1024) throw new InvalidDataException("Максимальний розмір зображення: 5 МБ.");
        byte[] bytes = await File.ReadAllBytesAsync(picker.FileName);
        if (bytes.Length > 5 * 1024 * 1024) throw new InvalidDataException("Максимальний розмір зображення: 5 МБ.");
        BitmapSource source = await Task.Run(() => DecodeAvatar(bytes, info.Extension));
        var panel = Stack();
        panel.Children.Add(Text("Квадратна область аватара", 20, "Text", true));
        var preview = new Image { Width = 256, Height = 256, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center };
        var previewFrame = new Border { Child = preview, Width = 256, Height = 256, HorizontalAlignment = HorizontalAlignment.Center };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        panel.Children.Add(previewFrame);
        var zoom = new Slider { Minimum = 1, Maximum = 4, Value = 1 };
        var x = new Slider { Minimum = 0, Maximum = 1, Value = 0.5 };
        var y = new Slider { Minimum = 0, Maximum = 1, Value = 0.5 };
        foreach (var (label, slider) in new[] { ("Масштаб", zoom), ("Горизонталь", x), ("Вертикаль", y) })
        {
            panel.Children.Add(Text(label));
            panel.Children.Add(slider);
        }
        BitmapSource Crop()
        {
            int size = Math.Max(1, (int)(Math.Min(source.PixelWidth, source.PixelHeight) / zoom.Value));
            var crop = new CroppedBitmap(source, new Int32Rect((int)((source.PixelWidth - size) * x.Value),
                (int)((source.PixelHeight - size) * y.Value), size, size));
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawImage(crop, new Rect(0, 0, 256, 256));
            var image = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
            image.Render(drawing);
            return image;
        }
        void Draw() => preview.Source = Crop();
        zoom.ValueChanged += (_, _) => Draw();
        x.ValueChanged += (_, _) => Draw();
        y.ValueChanged += (_, _) => Draw();
        Draw();
        var window = Modal("Аватар", panel, 480);
        var save = Action("Зберегти аватар", () => window.DialogResult = true, primary: true);
        panel.Children.Add(save);
        if (window.ShowDialog() != true) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(Crop(), PixelFormats.Bgra32, null, 0)));
        using var output = new MemoryStream();
        encoder.Save(output);
        if (account?.Token != session.Token) return;
        await Request<SyncAck>("account/avatar", new AvatarEdit(output.ToArray()), token: session.Token);
        InvalidatePlayerAvatar(session.Username ?? session.User);
        RefreshAccountAvatar(true);
    }

    private static BitmapSource DecodeAvatar(byte[] bytes, string extension)
    {
        if (!extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
                throw new InvalidDataException("Завелика роздільна здатність.");
            frame.Freeze();
            return frame;
        }

        // Only self-contained SVG is allowed. Reject active content and external resources.
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "svg") throw new InvalidDataException("Некоректний SVG.");
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            if (element.Name.LocalName is "script" or "foreignObject" or "style" or "image" or "iframe" or "filter")
                throw new InvalidDataException("SVG має бути самодостатнім без активного вмісту, вбудованих зображень або фільтрів.");
            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                string name = attribute.Name.LocalName, value = attribute.Value.Trim();
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name == "base" ||
                    (name is "href" or "src") && !value.StartsWith('#') ||
                    value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("@import", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SVG містить зовнішнє посилання або активний вміст.");
            }
        }
        using var sanitized = new MemoryStream();
        document.Save(sanitized);
        sanitized.Position = 0;
        using var svg = new SKSvg();
        var picture = svg.Load(sanitized) ?? throw new InvalidDataException("Не вдалося прочитати SVG.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
            throw new InvalidDataException("Некоректні розміри SVG.");
        float scale = 2048 / Math.Max(bounds.Width, bounds.Height);
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(scale);
        surface.Canvas.Translate(-bounds.Left, -bounds.Top);
        surface.Canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return ReadAvatar(png.ToArray());
    }
}
