using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace RabbitMQ_Server
{
    public class EventBusRabbitMQ
    {
        private readonly IRabbitMQPersistentConnection _persistentConnection;
        private IModel _consumerChannel;
        private string _queueName;
        private const string WorkerExchange = "work.exchange";
        private const string RetryExchange = "retry.exchange";
        private const string RetryQueue = "retry.queue";
        private const string DeadExchange = "dead.exchange";
        private const string DeadQueue = "dead.queue";

        private Dictionary<string, string> QueueMappingUrl = new Dictionary<string, string>()
        {
            { "EC", "https://localhost:44340/api/MQ/" },
            { "SubEC", "https://localhost:44312/api/MQ/" },
        };


        public EventBusRabbitMQ(IRabbitMQPersistentConnection persistentConnection, string queueName = null)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _queueName = queueName;
        }

        public IModel CreateConsumerChannel()
        {
            if (!_persistentConnection.IsConnected)
                _persistentConnection.TryConnect();

            var channel = _consumerChannel = _persistentConnection.CreateModel();

            channel.ExchangeDeclare(RetryExchange, "direct");
            channel.QueueDeclare
            (
                $"{_queueName}.{RetryQueue}", false, false, false, new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", WorkerExchange },
                { "x-dead-letter-routing-key", _queueName },
                { "x-message-ttl", 1000 }
            }
            );
            channel.QueueBind($"{_queueName}.{RetryQueue}", RetryExchange, $"{_queueName}.{RetryQueue}", null);


            channel.ExchangeDeclare(WorkerExchange, "direct");
            channel.QueueDeclare
            (
                _queueName, false, false, false, new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", RetryExchange },
                { "x-dead-letter-routing-key", $"{_queueName}.{RetryQueue}" }
            }
            );
            channel.QueueBind(_queueName, WorkerExchange, _queueName, null);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (sender, e) =>
            {
                bool success = false;

                if(QueueMappingUrl.TryGetValue(e.RoutingKey.Split('.')[0], out string url))
                    success = Post($"{url}{e.RoutingKey.Split('.')[1]}_Receive", Encoding.UTF8.GetString(e.Body.Span));

                if (success)
                    channel.BasicAck(deliveryTag: e.DeliveryTag, multiple: false);
                else if (!success)
                {
                    // 若失敗次數達到10次 則丟進deadQueue中，並以訊息通知系統管理員進行人工處理
                    long deadCount = 0;
                    if (e.BasicProperties.Headers != null && e.BasicProperties.Headers.ContainsKey("x-death"))
                    {
                        deadCount = ((e.BasicProperties.Headers["x-death"] as List<object>)[0] as Dictionary<string, dynamic>)["count"];

                        if (deadCount >= 10)
                        {
                            PublishDeadQueue(e.Body.ToArray(), e.RoutingKey);
                            channel.BasicAck(deliveryTag: e.DeliveryTag, multiple: false);
                            return;
                        }
                    }

                    channel.BasicReject(deliveryTag: e.DeliveryTag, false);
                }
            };

            channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            channel.CallbackException += (sender, ea) =>
            {
                _consumerChannel.Dispose();
                _consumerChannel = CreateConsumerChannel();
            };
            return channel;
        }

        public void Publish(string publishJson)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            try
            {
                using (var channel = _persistentConnection.CreateModel())
                {
                    channel.ExchangeDeclare(WorkerExchange, "direct");
                    channel.QueueDeclare(queue: _queueName, durable: false, exclusive: false, autoDelete: false,
                        new Dictionary<string, object>
                        {
                                {"x-dead-letter-exchange", RetryExchange},
                                {"x-dead-letter-routing-key",  $"{_queueName}.{RetryQueue}" }
                        });

                    channel.QueueBind(_queueName, WorkerExchange, _queueName, null);
                    var body = Encoding.UTF8.GetBytes(publishJson);

                    channel.ConfirmSelect();
                    channel.BasicPublish(exchange: WorkerExchange, routingKey: _queueName, mandatory: true, body: body);
                    channel.WaitForConfirmsOrDie();

                    channel.BasicAcks += (sender, eventArgs) =>
                    {
                        Console.WriteLine("Sent RabbitMQ");
                    };
                    channel.ConfirmSelect();
                }
            }
            catch (Exception ex)
            {
            }
        }

        public void PublishDeadQueue(byte[] message, string queueName)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            try
            {
                using (var channel = _persistentConnection.CreateModel())
                {
                    channel.ExchangeDeclare(DeadExchange, "direct");
                    channel.QueueDeclare(queue: $"{queueName}.{DeadQueue}", durable: false, exclusive: false, autoDelete: false, null);

                    channel.QueueBind($"{queueName}.{DeadQueue}", DeadExchange, $"{queueName}.{DeadQueue}", null);

                    channel.ConfirmSelect();
                    channel.BasicPublish(exchange: DeadExchange, routingKey: $"{queueName}.{DeadQueue}", mandatory: true, body: message);
                    channel.WaitForConfirmsOrDie();
                    channel.ConfirmSelect();
                }
            }
            catch (Exception ex)
            {
            }
        }

        public void Dispose()
        {
            if (_consumerChannel != null)
            {
                _consumerChannel.Dispose();
            }
        }

        /// <summary>
        /// Post
        /// </summary>
        /// <param name="uri">uri</param>
        /// <returns>response</returns>
        public bool Post(string url, string data)
        {
            using HttpClient client = new HttpClient();

            HttpResponseMessage httpResponseMessage = client.PostAsync(url, new StringContent(data, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            string response = httpResponseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            //string result = JsonConvert.DeserializeObject<dynamic>(response).result;

            return response == "true";
        }
    }
}