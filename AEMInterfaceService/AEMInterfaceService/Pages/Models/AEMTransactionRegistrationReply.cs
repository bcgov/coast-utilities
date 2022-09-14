using System;

namespace AEMInterfaceService.Pages.Models
{
    public class AEMTransactionRegistrationReply
    {
        String responseCode;
        public String ResponseCode
        {
            get { return responseCode; }
            set { responseCode = value; }
        }

        String responseMessage;
        public String ResponseMessage
        {
            get { return responseMessage; }
            set { responseMessage = value; }
        }
    }
}
