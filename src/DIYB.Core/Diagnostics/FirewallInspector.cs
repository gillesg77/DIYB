using System.Runtime.Versioning;

namespace DIYB.Core.Diagnostics;

public enum FirewallVerdict
{
    /// <summary>Hors Windows, ou service de pare-feu interrogeable.</summary>
    Unknown,

    /// <summary>Une règle autorise le programme sur le profil réseau courant.</summary>
    Allowed,

    /// <summary>Une règle de blocage vise le programme : elle l'emporte sur toute
    /// autorisation.</summary>
    Blocked,

    /// <summary>Aucune règle ne vise le programme, et l'entrant est refusé par
    /// défaut.</summary>
    NoRule,
}

public sealed record FirewallStatus(FirewallVerdict Verdict, string? RuleName = null)
{
    /// <summary>Vrai quand la réception ne peut pas fonctionner. Un verdict inconnu
    /// ne compte pas : mieux vaut se taire que d'accuser à tort.</summary>
    public bool PreventsDiscovery => Verdict is FirewallVerdict.Blocked or FirewallVerdict.NoRule;
}

/// <summary>Lit les règles du pare-feu Windows visant un exécutable donné.
/// Remplace l'estimation par comptage de paquets, trompeuse dès que la règle
/// « mDNS (UDP-Entrée) » de Windows est active : le multicast arrive alors même
/// quand le programme est bloqué.</summary>
public static class FirewallInspector
{
    private const int DirectionInbound = 1;
    private const int ActionBlock = 0;
    private const int ProtocolUdp = 17;
    private const int ProtocolAny = 256;

    public static FirewallStatus Inspect(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !OperatingSystem.IsWindows())
            return new FirewallStatus(FirewallVerdict.Unknown);

        try
        {
            return InspectWindows(executablePath);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or NotSupportedException or TypeLoadException or UnauthorizedAccessException)
        {
            // Service arrêté, stratégie de groupe restrictive, poste verrouillé :
            // on ne conclut rien plutôt que d'afficher un diagnostic faux.
            return new FirewallStatus(FirewallVerdict.Unknown);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FirewallStatus InspectWindows(string executablePath)
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type is null)
            return new FirewallStatus(FirewallVerdict.Unknown);

        dynamic? policy = Activator.CreateInstance(type);
        if (policy is null)
            return new FirewallStatus(FirewallVerdict.Unknown);

        int currentProfiles = policy.CurrentProfileTypes;
        var allowed = false;

        foreach (dynamic rule in policy.Rules)
        {
            string? application;
            try
            {
                application = rule.ApplicationName as string;
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrEmpty(application)
                || !string.Equals(application, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!(bool)rule.Enabled || (int)rule.Direction != DirectionInbound)
                continue;

            // Une règle inactive sur le profil courant ne joue aucun rôle.
            if (((int)rule.Profiles & currentProfiles) == 0)
                continue;

            var protocol = (int)rule.Protocol;
            if (protocol != ProtocolUdp && protocol != ProtocolAny)
                continue;

            // Un blocage l'emporte sur toute autorisation : inutile de poursuivre.
            if ((int)rule.Action == ActionBlock)
                return new FirewallStatus(FirewallVerdict.Blocked, rule.Name as string);

            allowed = true;
        }

        return allowed
            ? new FirewallStatus(FirewallVerdict.Allowed)
            : new FirewallStatus(FirewallVerdict.NoRule);
    }

    /// <summary>Verdict pour le programme en cours d'exécution.</summary>
    public static FirewallStatus InspectCurrentProcess() => Inspect(Environment.ProcessPath);
}
