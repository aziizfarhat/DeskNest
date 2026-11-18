using System;
using System.Globalization;
using System.Windows;

namespace DeskNest
{
    // Conversion et application des tailles de police. FontSize est un DependencyProperty
    // de type double : passing un int boxé à SetValue(DependencyProperty, object) lève
    // « '12' n'est pas une valeur valide pour la propriété 'FontSize' » (voir TodoContent).
    // Toutes les valeurs passent donc par ici : conversion InvariantCulture, bornage,
    // et repli sur la valeur par défaut sans jamais faire échouer le démarrage.
    public static class FontSizeHelper
    {
        public const double Min = 8;
        public const double Max = 28;

        public static double Safe(object? value, double fallback, double min = Min, double max = Max)
        {
            try
            {
                double size = value switch
                {
                    null => fallback,
                    double d => d,
                    _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
                };
                if (double.IsNaN(size) || double.IsInfinity(size)) return fallback;
                return Math.Clamp(size, min, max);
            }
            catch (Exception ex)
            {
                Diag.Log("police : valeur invalide « " + value + " » : " + ex.Message);
                return fallback;
            }
        }

        // Pour les affectations via SetValue(DependencyProperty, object) : garantit
        // qu'un double (jamais un int) est fourni à WPF.
        public static void Apply(DependencyObject target, DependencyProperty property,
            object? value, double fallback, double min = Min, double max = Max)
        {
            double size = Safe(value, fallback, min, max);
            try { target.SetValue(property, size); }
            catch (Exception ex)
            {
                Diag.Log("police : application impossible : " + ex.Message);
                target.SetValue(property, fallback);
            }
        }
    }
}
