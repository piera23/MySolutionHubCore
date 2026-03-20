using Hangfire.Dashboard;

namespace Api.Hangfire
{
    /// <summary>
    /// Restringe l'accesso al dashboard Hangfire ai soli utenti con ruolo Admin.
    /// </summary>
    public class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            // Deve essere autenticato e con ruolo Admin
            return httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.User.IsInRole("Admin");
        }
    }
}
