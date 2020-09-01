using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace RabbitMQ_Server
{
    public class RabbitMQPersistentConnection : IRabbitMQPersistentConnection
    {
        private readonly IConnectionFactory _connectionFactory;
        private EventBusRabbitMQ _eventBusRabbitMQ;
        private IConnection _connection;
        private bool _disposed;

        private Dictionary<string, List<string>> QueueConfig = new Dictionary<string, List<string>>()
        {
            { "EC", new List<string>() { "Test1" } },
            { "SubEC", new List<string>() { "Test1" } },
        };

        public RabbitMQPersistentConnection(IConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            if (!IsConnected)
            {
                TryConnect();
            }
        }

        public void CreateConsumerChannel()
        {
            if (!IsConnected)
            {
                TryConnect();
            }

            foreach(KeyValuePair<string, List<string>> item in QueueConfig)
            {
                item.Value.ForEach(x => new EventBusRabbitMQ(this, $"{item.Key}.{x}").CreateConsumerChannel());
            }

            //// 之後透過讀取config或環境變數 來自動mapping監聽queue
            ////CHannel 01
            //_eventBusRabbitMQ = new EventBusRabbitMQ(this, "EC");
            //_eventBusRabbitMQ.CreateConsumerChannel();

            ////CHannel 02
            //_eventBusRabbitMQ = new EventBusRabbitMQ(this, "SubEC");
            //_eventBusRabbitMQ.CreateConsumerChannel();
        }

        public void PublishQueue(PublishModel publishModel)
        {
            if (!IsConnected)
            {
                TryConnect();
            }

            // 發送
            new EventBusRabbitMQ(this, $"{publishModel.QueueName}.{publishModel.DataType}").Publish(publishModel.Message);
        }

        public void Disconnect()
        {
            if (_disposed)
            {
                return;
            }
            Dispose();
        }

        public bool IsConnected
        {
            get
            {
                return _connection != null && _connection.IsOpen && !_disposed;
            }
        }

        public IModel CreateModel()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("No RabbitMQ connections are available to perform this action");
            }
            return _connection.CreateModel();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _connection.Dispose();
            }
            catch (IOException ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public bool TryConnect()
        {
            try
            {
                Console.WriteLine("RabbitMQ Client is trying to connect");
                _connection = _connectionFactory.CreateConnection();
            }
            catch (BrokerUnreachableException e)
            {
                Thread.Sleep(5000);
                Console.WriteLine("RabbitMQ Client is trying to reconnect");
                _connection = _connectionFactory.CreateConnection();
            }

            if (IsConnected)
            {
                _connection.ConnectionShutdown += OnConnectionShutdown;
                _connection.CallbackException += OnCallbackException;
                _connection.ConnectionBlocked += OnConnectionBlocked;

                Console.WriteLine($"RabbitMQ persistent connection acquired a connection {_connection.Endpoint.HostName} and is subscribed to failure events");

                return true;
            }
            else
            {
                //  implement send warning email here
                //-----------------------
                Console.WriteLine("FATAL ERROR: RabbitMQ connections could not be created and opened");
                return false;
            }
        }

        private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            if (_disposed) return;
            Console.WriteLine("A RabbitMQ connection is shutdown. Trying to re-connect...");
            TryConnect();
        }

        private void OnCallbackException(object sender, CallbackExceptionEventArgs e)
        {
            if (_disposed) return;
            Console.WriteLine("A RabbitMQ connection throw exception. Trying to re-connect...");
            TryConnect();
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs reason)
        {
            if (_disposed) return;
            Console.WriteLine("A RabbitMQ connection is on shutdown. Trying to re-connect...");
            TryConnect();
        }
    }
}