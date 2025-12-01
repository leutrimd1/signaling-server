using Fleck;
using System.Text.Json;
using System.Collections.Concurrent;

namespace WebRTCServer
{
    public class SocketInfo
    {
        public required IWebSocketConnection Connection { get; set; }
        public string Offer { get; set; } = "";
    }

    public class Program
    {
        static void Main()
        {
            var sockets = new ConcurrentDictionary<Guid, SocketInfo>();

            // ---------------------------------------------
            // Helper: broadcast all socketGuids + offers
            // ---------------------------------------------
            void BroadcastServerState()
            {
                var state = new
                {
                    type = "state",
                    sockets = sockets.Select((kv) => new
                    {
                        socketGuid = kv.Key.ToString(),
                        offer = kv.Value.Offer
                    }).ToArray()
                };

                string json = JsonSerializer.Serialize(state);

                foreach (var s in sockets.Values)
                    s.Connection.Send(json);
            }

            // ---------------------------------------------
            // Helper: send an answer to the target socket
            // ---------------------------------------------
            void SendAnswerToTarget(Guid targetGuid, string answer)
            {
                if (!sockets.TryGetValue(targetGuid, out var info))
                    return;

                var msg = new
                {
                    type = "answer",
                    answer = answer
                };

                info.Connection.Send(JsonSerializer.Serialize(msg));
            }

            // ---------------------------------------------
            // WebSocket server
            // ---------------------------------------------
            var server = new WebSocketServer("ws://0.0.0.0:8181");

            server.Start(socket =>
            {
                Guid id = Guid.NewGuid();

                // -----------------------------------------
                // On connection open
                // -----------------------------------------
                socket.OnOpen = () =>
                {
                    sockets[id] = new SocketInfo { Connection = socket };
                    Console.WriteLine($"Socket {id} connected");

                    // Send the ID to the client immediately
                    socket.Send(JsonSerializer.Serialize(new
                    {
                        type = "id",
                        value = id.ToString()
                    }));

                    // Broadcast immediately about the new socket
                    BroadcastServerState();
                };

                // -----------------------------------------
                // On connection close
                // -----------------------------------------
                socket.OnClose = () =>
                {
                    sockets.TryRemove(id, out _);
                    Console.WriteLine($"Socket {id} disconnected");
                    BroadcastServerState();
                };

                // -----------------------------------------
                // On message
                // -----------------------------------------
                socket.OnMessage = raw =>
                {
                    JsonElement root;

                    try
                    {
                        root = JsonDocument.Parse(raw).RootElement;
                    }
                    catch
                    {
                        return;
                    }

                    if (!root.TryGetProperty("type", out var typeElement))
                        return;

                    string type = typeElement.GetString() ?? "";

                    // ---------------------------------------------------
                    // Handle: client sent an OFFER
                    // Save it & broadcast state to everyone
                    // ---------------------------------------------------
                    if (type == "offer")
                    {
                        if (root.TryGetProperty("offer", out var offerProp))
                        {
                            if (sockets.TryGetValue(id, out var info))
                                info.Offer = offerProp.GetString() ?? "";

                            BroadcastServerState();
                        }
                        return;
                    }

                    // ---------------------------------------------------
                    // Handle: client sent an ANSWER to a target socket
                    // ---------------------------------------------------
                    if (type == "answer")
                    {
                        if (root.TryGetProperty("targetSocketGuid", out var targetProp) &&
                            Guid.TryParse(targetProp.GetString(), out var targetGuid) &&
                            root.TryGetProperty("answer", out var answerProp))
                        {
                            SendAnswerToTarget(targetGuid, answerProp.GetString() ?? "");
                        }
                        return;
                    }
                };
            });

            Console.WriteLine("WebSocket server running at ws://0.0.0.0:8181");
            new System.Threading.ManualResetEvent(false).WaitOne();
        }
    }
}
