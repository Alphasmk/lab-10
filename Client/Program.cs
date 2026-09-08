using System.Net.Http.Json;
using EventManagement;

namespace Client
{
    class Program
    {
        static readonly HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };

        static async Task Main(string[] args)
        {
            while (true)
            {
                Console.WriteLine("1. Создать мероприятие");
                Console.WriteLine("2. Зарегистрировать участника");
                Console.WriteLine("3. Отменить регистрацию");
                Console.WriteLine("4. Выпустить билет");
                Console.WriteLine("5. Показать все мероприятия");
                Console.WriteLine("6. Показать все регистрации");
                Console.WriteLine("7. Показать все билеты");
                Console.WriteLine("0. Выход");
                Console.Write("Выберите действие: ");

                string choice = Console.ReadLine();
                if (choice == "0") break;

                try
                {
                    HttpResponseMessage response = null;

                    switch (choice)
                    {
                        case "1":
                            Console.Write("Введите название мероприятия: ");
                            var ev = new Event { Name = Console.ReadLine() };
                            response = await client.PostAsJsonAsync("/api/events", ev);
                            break;
                        
                        case "2":
                            Console.Write("Введите ID мероприятия: ");
                            int evId = int.Parse(Console.ReadLine());
                            Console.Write("Введите ID участника: ");
                            int partId = int.Parse(Console.ReadLine());
                            var reg = new Registration { EventId = evId, ParticipantId = partId };
                            response = await client.PostAsJsonAsync("/api/registrations", reg);
                            break;
                        
                        case "3":
                            Console.Write("Введите ID регистрации для отмены: ");
                            int regIdToCancel = int.Parse(Console.ReadLine());
                            response = await client.DeleteAsync($"/api/registrations/{regIdToCancel}");
                            break;
                        
                        case "4":
                            Console.Write("Введите ID регистрации для билета: ");
                            int regIdForTicket = int.Parse(Console.ReadLine());
                            response = await client.PostAsync($"/api/tickets/{regIdForTicket}", null);
                            break;
                        
                        case "5":
                            response = await client.GetAsync("/api/events");
                            break;

                        case "6":
                            response = await client.GetAsync("/api/registrations");
                            break;
                        
                        case "7":
                            response = await client.GetAsync("/api/tickets");
                            break;
                        
                        default:
                            Console.WriteLine("Неверная операция.");
                            continue;
                    }

                    if (response != null)
                    {
                        string result = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"\nСтатус: {response.StatusCode}");
                        Console.WriteLine($"Ответ сервера: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }
    }
}