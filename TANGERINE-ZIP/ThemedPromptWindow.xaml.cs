namespace TANGERINE_ZIP;

// Use the same live palette as the owner for prompts introduced by archive tools.
// A separate WPF window avoids system MessageBox colors overriding user settings.
internal sealed partial class ThemedPromptWindow : Window
{
    private MessageBoxResult _choice = MessageBoxResult.Cancel;

    private ThemedPromptWindow(Window? owner, string title, string message,
        (string Label, MessageBoxResult Result)[] choices)
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.4, 0.4);
        FontSize = SystemFonts.MessageFontSize;
        MouseWhiteThickening.Attach(this, 105);
        Title = title;
        messageText.Text = message;
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = owner is null;
        foreach ((string label, MessageBoxResult result) in choices)
        {
            Button button = new() { Content = label };
            button.Click += (_, _) => { _choice = result; DialogResult = true; };
            buttonsPanel.Children.Add(button);
        }
    }

    public static MessageBoxResult Ask(Window? owner, string title, string message,
        params (string Label, MessageBoxResult Result)[] choices)
    {
        ThemedPromptWindow prompt = new(owner, title, message, choices);
        prompt.ShowDialog();
        return prompt._choice;
    }

    public static void Inform(Window? owner, string title, string message) =>
        Ask(owner, title, message, (LanguageManager.Get("Confirm"), MessageBoxResult.OK));
}
