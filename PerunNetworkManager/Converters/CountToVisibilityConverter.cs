    public class CountToVisibilityConverter : IValueConverter
    {
        public int Threshold { get; set; } = 0;
        public Visibility AboveThresholdVisibility { get; set; } = Visibility.Visible;
        public Visibility BelowOrEqualThresholdVisibility { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;

            if (value is int intValue)
            {
                count = intValue;
            }
            else if (value is long longValue)
            {
                count = (int)longValue;
            }
            else if (value is double doubleValue)
            {
                count = (int)doubleValue;
            }
            else if (value is System.Collections.ICollection collection)
            {
                count = collection.Count;
            }
            else if (value is string strValue && int.TryParse(strValue, out int parsed))
            {
                count = parsed;
            }

            // Allow threshold override via parameter
            if (parameter != null && int.TryParse(parameter.ToString(), out int paramThreshold))
            {
                return count > paramThreshold ? AboveThresholdVisibility : BelowOrEqualThresholdVisibility;
            }

            return count > Threshold ? AboveThresholdVisibility : BelowOrEqualThresholdVisibility;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
