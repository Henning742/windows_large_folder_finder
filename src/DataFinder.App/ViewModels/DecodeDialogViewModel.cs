using System.Collections.ObjectModel;
using DataFinder.App.Infrastructure;
using DataFinder.Core.Preview.Raw;

namespace DataFinder.App.ViewModels;

/// <summary>
/// The state of the *Decode settings...* dialog: which file suffixes the preview reads as data
/// files, and the schematics it reads them with. It is built once and kept, so the choices are
/// still there the next time the dialog is opened.
/// </summary>
public sealed class DecodeDialogViewModel : ObservableObject
{
    private string _extensionsText = RawFileTypes.DefaultText;
    private string? _extensionsError;
    private IReadOnlyList<string> _extensions = RawFileTypes.Default;

    public DecodeDialogViewModel()
    {
        ResetExtensionsCommand = new RelayCommand(ResetExtensions, () => ExtensionsText != RawFileTypes.DefaultText);
        RestoreSchematicsCommand = new RelayCommand(RestoreSchematics);

        ResetSchematics();
        ValidateExtensions();
    }

    /// <summary>Every schematic on offer, in the order the list shows them.</summary>
    public ObservableCollection<RawSchema> Schemas { get; } = new();

    public RelayCommand ResetExtensionsCommand { get; }

    public RelayCommand RestoreSchematicsCommand { get; }

    /// <summary>What the user typed: the file suffixes to read as data files.</summary>
    public string ExtensionsText
    {
        get => _extensionsText;
        set
        {
            if (SetProperty(ref _extensionsText, value))
            {
                ValidateExtensions();
            }
        }
    }

    public string? ExtensionsError
    {
        get => _extensionsError;
        private set
        {
            if (SetProperty(ref _extensionsError, value))
            {
                OnPropertyChanged(nameof(HasExtensionsError));
            }
        }
    }

    public bool HasExtensionsError => !string.IsNullOrEmpty(ExtensionsError);

    /// <summary>The suffixes as a tidy list, which is what the file listing works with.</summary>
    public IReadOnlyList<string> Extensions
    {
        get => _extensions;
        private set => SetProperty(ref _extensions, value);
    }

    /// <summary>The suffix list in one line, shown under the box.</summary>
    public string ExtensionSummary => RawFileTypes.Describe(Extensions);

    /// <summary>The schematics that can actually be used, in list order.</summary>
    public IReadOnlyList<RawSchema> UsableSchemas =>
        Schemas.Where(schema => schema.IsValid).ToList();

    /// <summary>Puts the suffix list back to what the app starts with.</summary>
    public void ResetExtensions() => ExtensionsText = RawFileTypes.DefaultText;

    /// <summary>Puts the list of schematics back to the ones the app ships with.</summary>
    public void ResetSchematics()
    {
        Schemas.Clear();
        foreach (RawSchema schema in BuiltInRawSchemas.Create())
        {
            Schemas.Add(schema);
        }

        OnPropertyChanged(nameof(UsableSchemas));
    }

    /// <summary>
    /// Tells the reader that something changed that the open preview should know about. It is
    /// raised while the dialog is open, so the picture follows the box as it is typed in.
    /// </summary>
    public event Action? Changed;

    public void RaiseChanged()
    {
        OnPropertyChanged(nameof(UsableSchemas));
        Changed?.Invoke();
    }

    private void RestoreSchematics()
    {
        ResetSchematics();
        RaiseChanged();
    }

    private void ValidateExtensions()
    {
        if (!RawFileTypes.TryParse(ExtensionsText, out IReadOnlyList<string> suffixes, out string? error))
        {
            ExtensionsError = error;
            OnPropertyChanged(nameof(ExtensionSummary));
            return;
        }

        ExtensionsError = null;

        // A half typed list is read on every keystroke, so the window is only told about a list
        // that really changed - otherwise the preview would be decoded again per letter.
        if (Extensions.SequenceEqual(suffixes, StringComparer.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(ExtensionSummary));
            return;
        }

        Extensions = suffixes;
        OnPropertyChanged(nameof(ExtensionSummary));
        RaiseChanged();
    }
}
