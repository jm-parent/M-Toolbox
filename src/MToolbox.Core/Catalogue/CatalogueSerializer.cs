using System.Text.Json;
using System.Text.Json.Serialization;
using MToolbox.Core.Models;

namespace MToolbox.Core.Catalogue;

public static class CatalogueSerializer
{
    public const int SupportedSchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProjectCatalogue Deserialize(string json)
    {
        var catalogue = JsonSerializer.Deserialize<ProjectCatalogue>(json, Options)
            ?? throw new InvalidDataException("Catalogue vide.");

        if (catalogue.SchemaVersion > SupportedSchemaVersion)
            throw new InvalidDataException(
                $"Version de schéma {catalogue.SchemaVersion} non supportée : mettez M'Toolbox à jour.");

        var duplicate = catalogue.Projects
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Identifiant de projet en double : '{duplicate.Key}'.");

        return catalogue;
    }

    public static string Serialize(ProjectCatalogue catalogue) =>
        JsonSerializer.Serialize(catalogue, new JsonSerializerOptions(Options) { WriteIndented = true });
}
