using Fleck;

var server = new WebSocketServer("ws://0.0.0.0:8181");

server.Start(socket =>
{
    socket.OnOpen = () => Console.WriteLine("Connected");
    socket.OnMessage = msg => socket.Send("Echo: " + msg);
});

new ManualResetEvent(false).WaitOne();