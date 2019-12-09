using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace TestConsole
{
    public class Program
    {
        private readonly CosmosClient _client;
        private readonly LoggingRequestHandler _logger = new LoggingRequestHandler();

        // Entry point
        static void Main()
        {
            var program = new Program();
            program.RunTestAsync().Wait();
        }

        public Program()
        {
            var accountEndpoint = "https://localhost:8081";
            var authKeyOrResourceToken = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

            var clientOptions = new CosmosClientOptions();
            clientOptions.CustomHandlers.Add(_logger);

            _client = new CosmosClient(accountEndpoint, authKeyOrResourceToken, clientOptions);
        }

        public async Task RunTestAsync()
        {
            var container = await GetContainerAsync();
            await CreateItemsAsync(container);

            _logger.Enabled = true; // Start logging now

            Console.WriteLine();
            Console.WriteLine("---- There should only be one call after this line ----");
            Console.WriteLine();

            var query = new QueryDefinition($"select r from root r where r.{nameof(Example.Partition)} = @partitionKey order by r._ts")
                .WithParameter("@partitionKey", Example.PartitionValue);

            var feed1 = container.GetItemQueryIterator<Example>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = Example.PartitionKey,
                MaxItemCount = 5,
                MaxBufferedItemCount = 0,
                MaxConcurrency = 0
            });

            var firstPage = await feed1.ReadNextAsync();
            Console.WriteLine($"Retrieved first page, {firstPage.Count} items.");

            Console.WriteLine();
            Console.WriteLine("---- There should only be one call after this line ----");
            Console.WriteLine();

            var feed2 = container.GetItemQueryIterator<Example>(query, continuationToken: firstPage.ContinuationToken, requestOptions: new QueryRequestOptions
            {
                PartitionKey = Example.PartitionKey,
                MaxItemCount = 5,
                MaxBufferedItemCount = 0,
                MaxConcurrency = 0
            });

            var secondPage = await feed2.ReadNextAsync();
            Console.WriteLine($"Retrieved second page, {secondPage.Count} items.");
        }

        private async Task<Container> GetContainerAsync()
        {
            const string databaseId = "FeedTest";
            const string containerId = "Test";

            var database = _client.GetDatabase(databaseId);
            try { await database.DeleteAsync(); } catch { }

            var databaseResponse = await _client.CreateDatabaseIfNotExistsAsync(databaseId);
            database = databaseResponse.Database;

            var containerResponse = await database.CreateContainerAsync(new ContainerProperties
            {
                Id = containerId,
                PartitionKeyPath = $"/{nameof(Example.Partition)}"
            });
            return containerResponse.Container;
        }

        private async Task CreateItemsAsync(Container container)
        {
            for (var i = 0; i < 100; i++)
            {
                await container.CreateItemAsync(new Example { Index = i }, Example.PartitionKey);
            }
        }
    }

    public class Example
    {
        public const string PartitionValue = "test";
        public static readonly PartitionKey PartitionKey = new PartitionKey(PartitionValue);

        public string id => Guid.NewGuid().ToString();

        public int Index { get; set; }

        public string Partition => PartitionValue;
    }

    public class LoggingRequestHandler : RequestHandler
    {
        public bool Enabled { get; set; }

        public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (Enabled)
            {
                Console.WriteLine($"{request.Method} {request.RequestUri}");

                using (var reader = new StreamReader(response.Content, Encoding.UTF8, true, 1024, true))
                {
                    Console.WriteLine(reader.ReadToEnd());
                }

                Console.WriteLine();
            }

            return response;
        }
    }
}
