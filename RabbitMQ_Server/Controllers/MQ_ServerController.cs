using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RabbitMQ_Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MQ_ServerController : ControllerBase
    {
        private RabbitMQPersistentConnection _mqConnection;
        public MQ_ServerController(RabbitMQPersistentConnection mqConnection)
        {
            this._mqConnection = mqConnection;
        }

        [HttpPost("Publish")]
        public bool Publish(PublishModel publishModel)
        {
            _mqConnection.PublishQueue(publishModel);

            return true;
        }
    }
}