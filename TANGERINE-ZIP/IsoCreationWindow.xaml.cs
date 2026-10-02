using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: ISOWN. ISO filesystem and El Torito settings remain separate
// from the compression dialog and are validated before the worker starts.
internal sealed partial class IsoCreationWindow : Window
{
    public IsoCreationOptions? Options { get; private set; }

    public IsoCreationWindow()
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.60, 0.70);
        FontSize = SystemFonts.MessageFontSize;
        MouseWhiteThickening.Attach(this, 105);
        Title = LanguageManager.Get("IsoOptionsTitle");
        descriptionText.Text = LanguageManager.Get("IsoOptionsDescription");
        volumeLabel.Text = LanguageManager.Get("IsoVolumeLabel");
        manufacturerLabel.Text = LanguageManager.Get("IsoManufacturerLabel");
        jolietCheck.Content = LanguageManager.Get("IsoUseJoliet");
        deduplicateCheck.Content = LanguageManager.Get("IsoTrackEqualFiles");
        bootableCheck.Content = LanguageManager.Get("IsoBootable");
        bootImageLabel.Text = LanguageManager.Get("IsoBootImage");
        chooseBootImageButton.Content = LanguageManager.Get("IsoChooseBootImage");
        emulationLabel.Text = LanguageManager.Get("IsoBootEmulation");
        segmentLabel.Text = LanguageManager.Get("IsoLoadSegment");
        isolinuxCheck.Content = LanguageManager.Get("IsoUpdateIsolinux");
        bootNote.Text = LanguageManager.Get("IsoBootNote");
        confirmButton.Content = LanguageManager.Get("Confirm");
        cancelButton.Content = LanguageManager.Get("Cancel");
        string[] emulations = ["IsoEmulationNone", "IsoEmulation1200", "IsoEmulation1440",
            "IsoEmulation2880", "IsoEmulationHardDisk"];
        for (int index = 0; index < emulations.Length; index++)
            ((ComboBoxItem)emulationCombo.Items[index]).Content = LanguageManager.Get(emulations[index]);
        Bootable_Changed(this, new RoutedEventArgs());
    }

    private void Bootable_Changed(object sender, RoutedEventArgs e)
    {
        if (bootPanel is null) return;
        bool enabled = bootableCheck.IsChecked == true;
        bootPanel.IsEnabled = emulationCombo.IsEnabled = segmentBox.IsEnabled = isolinuxCheck.IsEnabled = enabled;
    }

    private void ChooseBootImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenFileDialog picker = new()
            {
                Title = LanguageManager.Get("IsoChooseBootImage"),
                Filter = LanguageManager.Get("IsoBootImageFilter"),
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) == true) bootImageBox.Text = picker.FileName;
        }
        catch (Exception error) { ShowError("ISOWN0001", error); } //ISOWN0001
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string volume = volumeBox.Text.Trim();
            string manufacturer = manufacturerBox.Text.Trim();
            if (volume.Length is < 1 or > 32 || manufacturer.Length > 128 ||
                volume.Any(character => character < ' ' || character > '~') ||
                manufacturer.Any(character => character < ' ' || character > '~'))
                throw new StageException("ISOWN0002", LanguageManager.Get("IsoInvalidMetadata")); //ISOWN0002
            bool bootable = bootableCheck.IsChecked == true;
            if (!int.TryParse(segmentBox.Text, out int segment) || segment is < 0 or > 65535 ||
                bootable && !File.Exists(bootImageBox.Text))
                throw new StageException("ISOWN0003", LanguageManager.Get("IsoInvalidBootImage")); //ISOWN0003
            string emulation = ((ComboBoxItem)emulationCombo.SelectedItem).Tag.ToString()!;
            if (bootable)
            {
                long size = new FileInfo(bootImageBox.Text).Length;
                long expected = emulation switch
                {
                    "Diskette1200KiB" => 1200L * 1024,
                    "Diskette1440KiB" => 1440L * 1024,
                    "Diskette2880KiB" => 2880L * 1024,
                    _ => 0
                };
                if (size == 0 || expected > 0 && size != expected)
                    throw new StageException("ISOWN0003", LanguageManager.Get("IsoInvalidBootImage")); //ISOWN0003
            }
            Options = new(volume, manufacturer, jolietCheck.IsChecked == true,
                deduplicateCheck.IsChecked == true, bootable,
                bootable ? Path.GetFullPath(bootImageBox.Text) : null, emulation, segment,
                bootable && isolinuxCheck.IsChecked == true);
            DialogResult = true;
        }
        catch (Exception error) { ShowError("ISOWN0004", error); } //ISOWN0004
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string fallback, Exception error) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), MessageTipGenerator.GenerateTip(
            error is StageException staged ? staged.StageCode : fallback,
            error is StageException ? error.Message : LanguageManager.Get("IsoOptionsFailed")));
}
