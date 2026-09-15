using System.Globalization;
using DataFinder.App.Infrastructure;
using DataFinder.Core.Preview.Raw;

namespace DataFinder.App.ViewModels;

/// <summary>
/// One schematic being edited in the Decode settings dialog. Every box is kept as text, so a half
/// typed number is not thrown away, and whatever reads cleanly is written straight into the
/// schematic - which is the object the preview reads from, so the picture follows the typing.
/// </summary>
public sealed class RawSchemaViewModel : ObservableObject
{
    private readonly RawSchema _schema;

    private string _nameText;
    private string _widthText;
    private string _heightText;
    private string _headerLengthText;
    private string _splitColumnText;
    private string _frameIndexText;
    private string _bordersText;
    private RawDataType _dataType;
    private bool _normalize;
    private bool _removeWhite;
    private bool _isShown = true;
    private string? _validationMessage;

    public RawSchemaViewModel(RawSchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));

        _nameText = schema.Name;
        _widthText = Number(schema.Width);
        _heightText = Number(schema.Height);
        _headerLengthText = Number(schema.HeaderLength);
        _splitColumnText = Number(schema.SplitColumn);
        _frameIndexText = Number(schema.FrameIndex);
        _bordersText = schema.Borders.IsNone ? string.Empty : schema.Borders.ToString();
        _dataType = schema.DataType;
        _normalize = schema.Normalize;
        _removeWhite = schema.RemoveWhite;

        Refresh();
    }

    /// <summary>Raised whenever the schematic changed, so an open preview can be drawn again.</summary>
    public event Action? Changed;

    /// <summary>The schematic itself, which is what the preview reads from.</summary>
    public RawSchema Schema => _schema;

    /// <summary>The layouts the data type drop down offers.</summary>
    public IReadOnlyList<RawDataTypeOption> DataTypes { get; } = RawDataTypeOption.All;

    /// <summary>
    /// Tick when this schematic should be part of the "show them all at once" view. It is on to
    /// begin with, so the whole set can be compared without ticking anything first.
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (SetProperty(ref _isShown, value))
            {
                Changed?.Invoke();
            }
        }
    }

    public string Name
    {
        get => _nameText;
        set
        {
            if (SetProperty(ref _nameText, value))
            {
                Refresh();
                OnPropertyChanged(nameof(ListName));
            }
        }
    }

    /// <summary>The name as the list row shows it, which is also what the preview drop down shows.</summary>
    public string ListName => _schema.Name;

    public string WidthText
    {
        get => _widthText;
        set
        {
            if (SetProperty(ref _widthText, value))
            {
                Refresh();
            }
        }
    }

    public string HeightText
    {
        get => _heightText;
        set
        {
            if (SetProperty(ref _heightText, value))
            {
                Refresh();
            }
        }
    }

    public string HeaderLengthText
    {
        get => _headerLengthText;
        set
        {
            if (SetProperty(ref _headerLengthText, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Where the packed 8 bit picture starts. Empty or 0 means the middle of the frame.</summary>
    public string SplitColumnText
    {
        get => _splitColumnText;
        set
        {
            if (SetProperty(ref _splitColumnText, value))
            {
                Refresh();
            }
        }
    }

    public string FrameIndexText
    {
        get => _frameIndexText;
        set
        {
            if (SetProperty(ref _frameIndexText, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Top, bottom, left and right rows and columns to throw away, as "1,1,0,0".</summary>
    public string BordersText
    {
        get => _bordersText;
        set
        {
            if (SetProperty(ref _bordersText, value))
            {
                Refresh();
            }
        }
    }

    public RawDataType DataType
    {
        get => _dataType;
        set
        {
            if (SetProperty(ref _dataType, value))
            {
                OnPropertyChanged(nameof(CanNormalize));
                Refresh();
            }
        }
    }

    /// <summary>True for the layouts that can be stretched to the full 0-255 range.</summary>
    public bool CanNormalize => RawDataTypes.CanNormalize(_dataType);

    public bool Normalize
    {
        get => _normalize;
        set
        {
            if (SetProperty(ref _normalize, value))
            {
                Refresh();
            }
        }
    }

    public bool RemoveWhite
    {
        get => _removeWhite;
        set
        {
            if (SetProperty(ref _removeWhite, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Why the schematic cannot be used, or null when it can.</summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool IsValid => ValidationMessage is null;

    public string SizeText => _schema.SizeText;

    public string FrameSizeText => _schema.FrameSizeText;

    /// <summary>The layout, in the words the data type drop down uses.</summary>
    public string DataTypeText => RawDataTypes.Describe(_schema.DataType);

    public string Description => _schema.Description;

    /// <summary>
    /// Reads every box, writes what makes sense into the schematic and works out what is left to
    /// complain about. The first box that cannot be read is reported on its own, so the message
    /// points at the problem instead of listing everything at once.
    /// </summary>
    private void Refresh()
    {
        string? problem = ApplyFields();
        problem ??= _schema.Validate();
        ValidationMessage = problem;

        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(FrameSizeText));
        OnPropertyChanged(nameof(DataTypeText));
        OnPropertyChanged(nameof(Description));
        Changed?.Invoke();
    }

    private string? ApplyFields()
    {
        string name = _nameText.Trim();
        if (name.Length == 0)
        {
            return "Give the schematic a name, so it can be told apart in the list.";
        }

        _schema.Name = name;

        if (!TryRead(_widthText, out int width))
        {
            return $"'{_widthText}' is not a whole number of pixels for the width.";
        }

        _schema.Width = width;

        if (!TryRead(_heightText, out int height))
        {
            return $"'{_heightText}' is not a whole number of pixels for the height.";
        }

        _schema.Height = height;

        if (!TryRead(_headerLengthText, out int headerLength))
        {
            return $"'{_headerLengthText}' is not a whole number of bytes for the header length.";
        }

        _schema.HeaderLength = headerLength;

        if (!TryRead(_splitColumnText, out int splitColumn))
        {
            return $"'{_splitColumnText}' is not a whole number of columns for the packed 8 bit split.";
        }

        _schema.SplitColumn = splitColumn;

        if (!TryRead(_frameIndexText, out int frameIndex))
        {
            return $"'{_frameIndexText}' is not a whole number for the frame to show.";
        }

        _schema.FrameIndex = frameIndex;

        if (!RawBorders.TryParse(_bordersText, out RawBorders borders, out string? borderError))
        {
            return borderError;
        }

        _schema.Borders = borders;
        _schema.DataType = _dataType;
        _schema.Normalize = _normalize;
        _schema.RemoveWhite = _removeWhite;
        return null;
    }

    /// <summary>An empty box means zero, which is the sensible default for the optional numbers.</summary>
    private static bool TryRead(string? text, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
