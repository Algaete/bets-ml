using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CornersMLData.Data
{
    /// <summary>
    /// Canonical names used by every ingestion path. Add a new source spelling here,
    /// never in an individual scraper.
    /// </summary>
    public static class CanonicalNameCatalog
    {
        private static readonly IReadOnlyDictionary<string, string> TeamAliases = BuildAliases(
            ("Alemania", new[] { "Germany" }),
            ("Atlanta", new[] { "CA Atlanta" }),
            ("Atlanta United FC", new[] { "Atlanta United" }),
            ("Arabia Saudita", new[] { "Saudi Arabia" }),
            ("Argelia", new[] { "Algeria" }),
            ("Argentina", Array.Empty<string>()),
            ("Athletico Paranaense", new[] { "Athletico PR", "Athletico-PR", "Atletico PR", "Atletico-PR", "Atletico Paranaense" }),
            ("Atletico-MG", new[] { "Atletico MG", "Clube Atletico MG" }),
            ("Australia", Array.Empty<string>()),
            ("Austria", Array.Empty<string>()),
            ("Bahia", new[] { "Bahia BA", "EC Bahia BA" }),
            ("Bélgica", new[] { "Belgium" }),
            ("Bosnia y Herzegovina", new[] { "Bosnia and Herzegovina", "Bosnia-Herzegovina" }),
            ("Botafogo", new[] { "Botafogo RJ" }),
            ("Brasil", new[] { "Brazil" }),
            ("Cabo Verde", new[] { "Cape Verde", "Cape Verde Islands" }),
            ("Canadá", new[] { "Canada" }),
            ("Catar", new[] { "Qatar" }),
            ("Central Norte", new[] { "Central Norte Salta" }),
            ("Chapecoense", new[] { "Chapecoense SC" }),
            ("Cienciano", new[] { "Cienciano del Cusco" }),
            ("Colombia", Array.Empty<string>()),
            ("Corea del Norte", new[] { "North Korea" }),
            ("Corea del Sur", new[] { "South Korea", "Korea Republic" }),
            ("Costa de Marfil", new[] { "Ivory Coast", "Cote d'Ivoire", "Côte d’Ivoire" }),
            ("Croacia", new[] { "Croatia" }),
            ("Curazao", new[] { "Curacao", "Curaçao" }),
            ("Ecuador", Array.Empty<string>()),
            ("Egipto", new[] { "Egypt" }),
            ("Escocia", new[] { "Scotland" }),
            ("España", new[] { "Spain" }),
            ("Estados Unidos", new[] { "United States", "USA", "U.S.A." }),
            ("Francia", new[] { "France" }),
            ("Flamengo", new[] { "Flamengo RJ" }),
            ("Ghana", Array.Empty<string>()),
            ("Gimnasia Y Esgrima Jujuy", new[] { "Gimnasia Jujuy" }),
            ("Haití", new[] { "Haiti" }),
            ("Inglaterra", new[] { "England" }),
            ("Internacional", new[] { "Internacional RS" }),
            ("Irak", new[] { "Iraq" }),
            ("Irán", new[] { "Iran" }),
            ("Japón", new[] { "Japan" }),
            ("Jordania", new[] { "Jordan" }),
            ("Marruecos", new[] { "Morocco" }),
            ("Midland", new[] { "Ferrocarril Midland" }),
            ("Mushuc Runa", new[] { "Mushuc Runa SC" }),
            ("Charlotte FC", new[] { "Charlotte" }),
            ("San Diego FC", new[] { "San Diego" }),
            ("México", new[] { "Mexico" }),
            ("México U20", new[] { "Mexico U20" }),
            ("Noruega", new[] { "Norway" }),
            ("Nueva Zelanda", new[] { "New Zealand" }),
            ("Países Bajos", new[] { "Netherlands", "Holland" }),
            ("Panamá", new[] { "Panama" }),
            ("Paraguay", Array.Empty<string>()),
            ("Portugal", Array.Empty<string>()),
            ("Racing (Montevideo)", new[] { "Racing Club de Montevideo" }),
            ("RB Bragantino", new[] { "Bragantino", "Bragantino SP", "Red Bull Bragantino" }),
            ("RD del Congo", new[] { "DR Congo", "Democratic Republic of Congo" }),
            ("República Checa", new[] { "Czechia", "Czech Republic", "Chequia" }),
            ("Santos", new[] { "Santos SP" }),
            ("Senegal", Array.Empty<string>()),
            ("San Martin S.J.", new[] { "San Martin de San Juan" }),
            ("Sao Paulo", new[] { "Sao Paulo SP" }),
            ("CSKA Sofia", new[] { "PFC CSKA Sofia" }),
            ("FC Twente", new[] { "Twente", "FC Twente Enschede", "Twente Enschede" }),
            ("FK Tobol Kostanay", new[] { "Tobol Kostanay" }),
            ("Hibernian", new[] { "Hibernian FC" }),
            ("Panevezys", new[] { "FK Panevezys" }),
            ("Pyunik", new[] { "FC Pyunik Yerevan", "Pyunik Yerevan" }),
            ("Qarabag", new[] { "FK Qarabag", "Qarabağ", "Qarabağ FK" }),
            ("Sudáfrica", new[] { "South Africa" }),
            ("Suecia", new[] { "Sweden" }),
            ("Suiza", new[] { "Switzerland" }),
            ("Tromso", new[] { "Tromso IL", "Tromsø", "Tromsø IL" }),
            ("Túnez", new[] { "Tunisia" }),
            ("Turquía", new[] { "Turkiye", "Turkey", "Türkiye" }),
            ("Uruguay", Array.Empty<string>()),
            ("Uzbekistán", new[] { "Uzbekistan" }),
            ("Vasco da Gama", new[] { "Vasco da Gama RJ" }),
            ("Vitória", new[] { "Vitoria", "Vitoria BA", "Vitória BA" }),
            ("Zira", new[] { "Zira FK" }));

        private static readonly IReadOnlyDictionary<string, string> LeagueAliases = BuildAliases(
            ("Argentine Nacional B", new[] { "Argentina - Primera B Nacional", "Argentina Primera B Nacional", "Primera Nacional de Argentina" }),
            ("Brasileirão", new[] { "Brasil - Brasileirao - Serie A Betano", "Brasil - Brasileirao - Serie A", "Brazil - Serie A", "Brazil Serie A", "Campeonato Brasileiro" }),
            ("Copa del Mundo", new[] { "FIFA - World Cup", "FIFA World Cup", "World Cup", "Copa Mundial FIFA", "Mundial" }),
            ("Copa Chile", new[] { "Copa Chile Easy", "Chile Cup" }),
            ("Liga AUF Uruguaya", new[] { "Uruguay - Primera Division", "Uruguay - Primera División", "Uruguay Primera Division" }),
            ("MLS", new[] { "USA - Major League Soccer", "Major League Soccer", "USA MLS" }));

        public static IReadOnlyCollection<CanonicalNameAlias> GetTeamAliases() =>
            TeamAliases
                .Select(pair => new CanonicalNameAlias(pair.Key, pair.Value))
                .ToArray();

        public static IReadOnlyCollection<CanonicalNameAlias> GetLeagueAliases() =>
            LeagueAliases
                .Select(pair => new CanonicalNameAlias(pair.Key, pair.Value))
                .ToArray();

        public static string CanonicalizeTeam(string? value) => Canonicalize(value, TeamAliases);

        public static string CanonicalizeLeague(string? value) => Canonicalize(value, LeagueAliases);

        public static IReadOnlyCollection<string> GetEquivalentTeamNames(string? value)
        {
            var clean = Clean(value);
            if (clean.Length == 0)
                return Array.Empty<string>();

            var canonical = CanonicalizeTeam(clean);
            return TeamAliases
                .Where(pair => pair.Value.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .Append(canonical)
                .Append(clean)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyCollection<string> GetEquivalentLeagueNames(string? value)
        {
            var clean = Clean(value);
            if (clean.Length == 0)
                return Array.Empty<string>();

            var canonical = CanonicalizeLeague(clean);
            return LeagueAliases
                .Where(pair => pair.Value.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .Append(canonical)
                .Append(clean)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool IsKnownNationalTeam(string? value)
        {
            var key = NormalizeKey(value);
            return key.Length > 0 && TeamAliases.ContainsKey(key);
        }

        public static string NormalizeKey(string? value)
        {
            var normalized = Clean(value)
                .Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            var previousWasSpace = false;

            foreach (var character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(FoldLetter(character));
                    previousWasSpace = false;
                    continue;
                }

                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static char FoldLetter(char value) => char.ToLowerInvariant(value) switch
        {
            'ğ' => 'g',
            'ı' => 'i',
            'ł' => 'l',
            'ø' => 'o',
            _ => char.ToLowerInvariant(value)
        };

        private static string Canonicalize(string? value, IReadOnlyDictionary<string, string> aliases)
        {
            var clean = Clean(value);
            var key = NormalizeKey(clean);
            return key.Length > 0 && aliases.TryGetValue(key, out var canonical)
                ? canonical
                : clean;
        }

        private static IReadOnlyDictionary<string, string> BuildAliases(
            params (string canonical, string[] aliases)[] definitions)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (canonical, aliases) in definitions)
            {
                Add(canonical, canonical);
                foreach (var alias in aliases)
                    Add(alias, canonical);
            }

            return result;

            void Add(string alias, string canonical)
            {
                var key = NormalizeKey(alias);
                if (key.Length == 0)
                    return;

                if (result.TryGetValue(key, out var existing) && !existing.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The alias '{alias}' maps to both '{existing}' and '{canonical}'.");

                result[key] = canonical;
            }
        }

        private static string Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public sealed record CanonicalNameAlias(string AliasKey, string CanonicalName);
}
