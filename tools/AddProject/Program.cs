using MToolbox.Core.Catalogue;

// Appelé par .github/workflows/add-project.yml. Le corps de l'issue est une donnée non fiable : il n'arrive que par ISSUE_BODY.
// Usage : AddProject <catalogue.json> <dossier logos> <fichier d'erreur>
const int MaxLogoBytes = 2 * 1024 * 1024;
byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

if (args.Length != 3) return Fail("Usage : AddProject <catalogue.json> <dossier logos> <fichier d'erreur>", null);
var (cataloguePath, logosDir, errorFile) = (args[0], args[1], args[2]);

var body = Environment.GetEnvironmentVariable("ISSUE_BODY") ?? "";
if (!AddProjectIssue.TryReadProject(body, out var parsed, out var readError))
    return Fail(readError, errorFile);

var catalogueText = File.ReadAllText(cataloguePath);
var existingIds = CatalogueSerializer.Deserialize(catalogueText).Projects.Select(p => p.Id);

// Le logo n'est jamais repris du JSON : il est fixé plus bas si une pièce jointe valide existe.
var project = parsed! with { Logo = null };
var errors = ProjectSubmission.Validate(project, existingIds);
if (errors.Count > 0) return Fail(string.Join(Environment.NewLine, errors.Select(e => "- " + e)), errorFile);

byte[]? logo = null;
if (AddProjectIssue.FindLogoUrl(body) is { } logoUrl)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MToolbox-AddProject");
    try
    {
        using var response = await http.GetAsync(logoUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxLogoBytes) return Fail("Le logo dépasse 2 Mo.", errorFile);
        }

        logo = buffer.ToArray();
    }
    catch (HttpRequestException ex)
    {
        return Fail($"Impossible de télécharger le logo : {ex.Message}", errorFile);
    }

    if (logo.Length < pngSignature.Length || !logo.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
        return Fail("Le logo doit être un fichier PNG.", errorFile);

    project = project with { Logo = ProjectSubmission.LogoPathFor(project.Id) };
}

try
{
    File.WriteAllText(cataloguePath, ProjectSubmission.AppendToCatalogue(catalogueText, project));
}
catch (InvalidDataException ex)
{
    return Fail(ex.Message, errorFile);
}

if (logo is not null)
{
    Directory.CreateDirectory(logosDir);
    File.WriteAllBytes(Path.Combine(logosDir, $"{project.Id}.png"), logo);
}

if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } output)
    File.AppendAllText(output, $"project_id={project.Id}{Environment.NewLine}");

Console.WriteLine($"Projet '{project.Id}' ajouté.");
return 0;

static int Fail(string message, string? errorFile)
{
    Console.Error.WriteLine(message);
    if (errorFile is not null) File.WriteAllText(errorFile, message);
    return 1;
}
