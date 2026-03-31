namespace TravelDisruptionAgent.Application.Options;

/// <summary>Transport and host hardening (HTTPS redirect/HSTS).</summary>
public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>When true, enables HSTS and HTTPS redirection (typical behind TLS-terminated ingress).</summary>
    public bool RequireHttpsRedirection { get; set; }
}
