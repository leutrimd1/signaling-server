using Fleck;
using System.Text.Json;
using System.Collections.Concurrent;

namespace WebRTCServer
{

    public class Program
    {
        static void Main()
        {
            var sockets = new ConcurrentDictionary<Guid, IWebSocketConnection>();

            // ---------------------------------------------
            // Helper: broadcast all connected socket Guids
            // ---------------------------------------------
            void BroadcastServerState()
            {
                var state = new
                {
                    type = "state",
                    sockets = sockets.Keys.Select(guid => guid.ToString()).ToArray()
                };

                string json = JsonSerializer.Serialize(state);

                foreach (var s in sockets.Values)
                    s.Send(json);
            }

            // ---------------------------------------------
            // Helper: send an answer to the target socket. Takes the target socket guid and sends a message type answer
            // ---------------------------------------------
            void SendAnswerToTarget(Guid sourceGuid, Guid targetGuid, string answer)
            {
                if (!sockets.ContainsKey(targetGuid))
                    return;

                var msg = new
                {
                    type = "answer",
                    from = sourceGuid.ToString(),
                    to = targetGuid.ToString(),
                    offer = "",
                    answer
                };

                sockets[targetGuid].Send(JsonSerializer.Serialize(msg));
            }

            // ---------------------------------------------
            // Helper: send an offer to the target socket. Takes a source and target guid with an offer string to send.
            // ---------------------------------------------
            void SendOfferToTarget(Guid sourceGuid, Guid targetGuid, string offer)
            {
                if (!sockets.ContainsKey(targetGuid))
                    return;

                var msg = new
                {
                    type = "offer",
                    from = sourceGuid.ToString(),
                    to = targetGuid.ToString(),
                    offer,
                    answer = ""
                };

                sockets[targetGuid].Send(JsonSerializer.Serialize(msg));
            }

            // ---------------------------------------------
            // WebSocket server
            // ---------------------------------------------
            var server = new WebSocketServer("ws://0.0.0.0:8181");

            server.Start(socket =>
            {
                Guid thisSocketGuid = Guid.NewGuid();

                // -----------------------------------------
                // On connection open
                // -----------------------------------------
                socket.OnOpen = () =>
                {
                    sockets[thisSocketGuid] = socket;
                    Console.WriteLine($"Socket {thisSocketGuid} connected");

                    // Send the ID to the client immediately
                    socket.Send(JsonSerializer.Serialize(new
                    {
                        type = "id",
                        value = thisSocketGuid.ToString()
                    }));

                    // Broadcast immediately about the new socket
                    BroadcastServerState();
                };

                // -----------------------------------------
                // On connection close
                // -----------------------------------------
                socket.OnClose = () =>
                {
                    sockets.TryRemove(thisSocketGuid, out _);
                    Console.WriteLine($"Socket {thisSocketGuid} disconnected");
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
                        var messageType = root.GetProperty("type").GetString() ?? "";

                        // Handle messages from sockets
                        if (messageType == "offer")
                        {
                            var targetStr = root.GetProperty("to").GetString();
                            var offerSdp = root.GetProperty("offer").GetString();
                            if (Guid.TryParse(targetStr, out Guid targetGuid) && offerSdp != null)
                            {
                                Console.WriteLine($"Forwarding offer from {thisSocketGuid} to {targetGuid}");
                                SendOfferToTarget(thisSocketGuid, targetGuid, offerSdp);
                            }
                            return;
                        }

                        if (messageType == "answer")
                        {
                            var targetStr = root.GetProperty("to").GetString();
                            var answerSdp = root.GetProperty("answer").GetString();
                            if (Guid.TryParse(targetStr, out Guid targetGuid) && answerSdp != null)
                            {
                                Console.WriteLine($"Forwarding answer from {thisSocketGuid} to {targetGuid}");
                                SendAnswerToTarget(thisSocketGuid, targetGuid, answerSdp);
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing message from {thisSocketGuid}: {ex.Message}");
                    }
                };
            });

            Console.WriteLine("WebSocket server running at ws://0.0.0.0:8181");
            new System.Threading.ManualResetEvent(false).WaitOne();
        }
    }
}
