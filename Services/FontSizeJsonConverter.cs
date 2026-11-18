using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskNest
{
    // Une taille de police doit accepter 12 (entier), 12.0 (réel) et une chaîne
    // numérique sans faire échouer le chargement de tout le layout. Une valeur
    // réellement invalide donne NaN : Storage.Normalize() la remplace alors par
    // la valeur par défaut du réglage.
    public sealed class FontSizeJsonConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetDouble(out double number)) return number;
                    break;
                case JsonTokenType.String:
                    if (double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                        return parsed;
                    break;
            }
            return double.NaN;
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(double.IsNaN(value) || double.IsInfinity(value) ? 12 : value);
    }
}
