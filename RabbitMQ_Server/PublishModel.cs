using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RabbitMQ_Server
{
    public class PublishModel
    {
        public string QueueName { get; set; }

        public string DataType { get; set; }

        public string Message { get; set; }
    }
}
