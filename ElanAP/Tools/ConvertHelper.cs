using System;
using System.Windows.Controls;

namespace ElanAP.Tools
{
    static class ConvertHelper
    {
        public static double GetDouble(this TextBox box)
        {
            return SafeDoubleParse(box.Text);
        }

        public static double SafeDoubleParse(object value)
        {
            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return 0;
            }
        }
    }
}
