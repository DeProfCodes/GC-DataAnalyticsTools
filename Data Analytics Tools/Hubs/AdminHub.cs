using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Data_Analytics_Tools.Hubs
{
    public class AdminHub : Hub
    {
        protected IHubContext<AdminHub> _context;

        public AdminHub(IHubContext<AdminHub> context)
        {
            this._context = context;
        }
        public async Task SendApacheLogsProcess(string type, string message, string status)
        {
            await _context.Clients.All.SendAsync("ReceivedMessage", type, message, status);
        }
    }
}
