using System;
using System.Threading.Tasks;
using CornetInterfaceService.Pages.Models;
using CornetInterfaceService.Pages.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace CornetInterfaceService.Pages.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CornetTransactionController : Controller
    {
        private readonly IDynamicsCornetNotificationClient _dynamicsCornetNotificationClient;

        public CornetTransactionController(IDynamicsCornetNotificationClient dynamicsCornetNotificationClient)
        {
            _dynamicsCornetNotificationClient = dynamicsCornetNotificationClient;
        }

        [HttpPost]        
        public async Task<CornetTransactionRegistrationReply> RegisterCornetTransaction(CornetTransaction cornetTransaction)
        {
            CornetTransactionRegistrationReply cornetregreply = new CornetTransactionRegistrationReply();
            try
            {
                var result = await _dynamicsCornetNotificationClient.SendCornetNotificationAsync(cornetTransaction);

                if (result.IsSuccess)
                {
                    cornetregreply.ResponseCode = "200";
                    cornetregreply.ResponseMessage = "Success";
                }
                else
                {
                    cornetregreply.ResponseMessage = "Failure";
                    cornetregreply.ResponseCode = result.ResponseCode;
                    Log.Error("Cornet transaction {EventMessageId} failed with code {ResponseCode}", cornetTransaction.event_message_id, result.ResponseCode);
                }

                return cornetregreply;

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error Registering Cornet Transaction: {EventMessageId}", cornetTransaction.event_message_id);
                cornetregreply.ResponseMessage = "Failure";
                cornetregreply.ResponseCode = "200"; // counterintuitive, but cornet might expect this and not retry if it sees a different code
                return cornetregreply;
            }
        }

        /// <summary>
        /// DEPRECATED - TO BE REMOVED.
        ///
        /// Originally wrote into the now-deleted <c>CornetTransactionRegistration</c> in-memory
        /// store (an audit-trail pattern inherited from CASInterfaceService that was never given a
        /// read-side for Cornet - see CornetTransactionRegistration's removal). Since that store no
        /// longer exists, this endpoint is now a pure no-op that validates the request and always
        /// returns "Success" without doing anything with the data.
        ///
        /// Confirmed via Splunk that this path has
        /// never been called in PROD across the full retention history. Safe to remove outright.
        /// </summary>
        [Obsolete("No-op since CornetTransactionRegistration was removed; never called in PROD. Scheduled for removal.")]
        [HttpPost("InsertCornetTransaction")]
        public IActionResult InsertCornetTransaction(CornetTransaction cornetTransaction)
        {
            try
            {
                CornetTransactionRegistrationReply casregreply = new CornetTransactionRegistrationReply();
                casregreply.ResponseMessage = "Success";

                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in InsertCornetTransaction");
                return StatusCode(e.HResult);
            }

        }
    }
}
