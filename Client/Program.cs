namespace Client
{
    class Program
    {
        static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("SERVER_URL") ?? "http://localhost:5000")
        };
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Contains("--reserve"))
            {
                Environment.ExitCode = await ReservationCommand.RunAsync(args, client);
                return;
            }

            Console.Write("Введите идентификатор клиента: ");
            string? clientId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = "Client1";
            }

            ClientApplication application = new ClientApplication(client, Console.In, Console.Out, clientId);
            while (true)
            {
                Console.WriteLine($"\nКлиент: {clientId}");
                Console.WriteLine("1. Создать площадку");
                Console.WriteLine("2. Создать участника");
                Console.WriteLine("3. Создать мероприятие");
                Console.WriteLine("4. Показать мероприятия и свободные места");
                Console.WriteLine("5. Зарегистрировать участника");
                Console.WriteLine("6. Отменить регистрацию");
                Console.WriteLine("7. Показать регистрации");
                Console.WriteLine("8. Состояние узлов и голосования");
                Console.WriteLine("9. Доступность узла");
                Console.WriteLine("10. Проверка и выбор лидера");
                Console.WriteLine("11. Настроить голос узла");
                Console.WriteLine("0. Выход");
                Console.Write("Выберите действие: ");
                string? choice = Console.ReadLine();
                if (string.IsNullOrEmpty(choice) || choice == "0")
                {
                    break;
                }

                await application.ExecuteAsync(choice);
            }
        }
    }
}
