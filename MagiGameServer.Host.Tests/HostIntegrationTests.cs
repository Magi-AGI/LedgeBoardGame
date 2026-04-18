using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MagiGameServer.Codec;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Host.Tests
{
    [TestFixture]
    public class HostIntegrationTests
    {
        private TestHostFactory _factory;

        [SetUp]
        public void SetUp() => _factory = new TestHostFactory();

        [TearDown]
        public void TearDown() => _factory?.Dispose();

        private async Task<OpenSessionResponse> OpenAsync(int seatCount = 2)
        {
            using var client = _factory.CreateClient();
            var req = new OpenSessionRequest
            {
                GameId = "counter",
                SeatCount = seatCount,
                Seed = 0,
            };
            var resp = await client.PostAsJsonAsync("/session/open", req, EnvelopeCodec.Options);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "open must succeed");
            return await resp.Content.ReadFromJsonAsync<OpenSessionResponse>(EnvelopeCodec.Options);
        }

        private async Task<WebSocket> ConnectAsync(SessionId session, int seat, CancellationToken ct)
        {
            var wsClient = _factory.Server.CreateWebSocketClient();
            var uri = new UriBuilder(_factory.Server.BaseAddress) { Scheme = "ws" };
            uri.Path = $"/session/{session.Value}";
            uri.Query = $"seat={seat}";
            return await wsClient.ConnectAsync(uri.Uri, ct);
        }

        private static async Task<ServerFrame<CounterState>> ReceiveFrameAsync(WebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[16384];
            var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (res.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, res.Count);
            } while (!res.EndOfMessage);
            return JsonSerializer.Deserialize<ServerFrame<CounterState>>(ms.ToArray(), EnvelopeCodec.Options);
        }

        private static async Task SendActionAsync(WebSocket socket, SessionId session, int seat, long seq, int delta, long predictedHash, CancellationToken ct)
        {
            var frame = new ClientFrame<CounterAction>
            {
                Kind = ClientFrameKind.Action,
                Action = new ActionEnvelope<CounterAction>
                {
                    Session = session,
                    Seat = new SeatId(seat),
                    Seq = new ClientSeq(seq),
                    Action = new CounterAction { Delta = delta },
                    PredictedStateHash = predictedHash,
                },
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, EnvelopeCodec.Options);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }

        private static async Task SendTakebackAsync(WebSocket socket, SessionId session, int seat, long seqAtRequestTime, int steps, CancellationToken ct)
        {
            var frame = new ClientFrame<CounterAction>
            {
                Kind = ClientFrameKind.Takeback,
                Takeback = new TakebackRequest
                {
                    Session = session,
                    RequestingSeat = new SeatId(seat),
                    SeqAtRequestTime = new ClientSeq(seqAtRequestTime),
                    StepsRequested = steps,
                    Reason = "test",
                },
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, EnvelopeCodec.Options);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }

        [Test]
        public async Task Healthz_Returns200_WithRegisteredModules()
        {
            using var client = _factory.CreateClient();
            var resp = await client.GetAsync("/healthz");
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var body = await resp.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain("counter"));
        }

        [Test]
        public async Task TwoSeats_Apply_BroadcastsEchoToBothSeats()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            var ws1 = await ConnectAsync(session.Session, 1, cts.Token);

            // Drain JoinSnapshots.
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            var join1 = await ReceiveFrameAsync(ws1, cts.Token);
            Assert.That(join0.Kind, Is.EqualTo(ServerFrameKind.JoinSnapshot));
            Assert.That(join1.Kind, Is.EqualTo(ServerFrameKind.JoinSnapshot));
            Assert.That(join0.JoinSnapshot.State.Value, Is.EqualTo(0));

            // Seat 0 submits delta=5.
            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 5, predictedHash: 0, cts.Token);

            var echo0 = await ReceiveFrameAsync(ws0, cts.Token);
            var echo1 = await ReceiveFrameAsync(ws1, cts.Token);

            Assert.That(echo0.Kind, Is.EqualTo(ServerFrameKind.StateEcho));
            Assert.That(echo1.Kind, Is.EqualTo(ServerFrameKind.StateEcho));
            Assert.That(echo0.Echo.ForSeat.Value, Is.EqualTo(0));
            Assert.That(echo1.Echo.ForSeat.Value, Is.EqualTo(1));
            Assert.That(echo0.Echo.SubmittingSeat.Value, Is.EqualTo(0));
            Assert.That(echo1.Echo.SubmittingSeat.Value, Is.EqualTo(0));
            Assert.That(echo0.Echo.Revision.Value, Is.EqualTo(1));
            Assert.That(echo1.Echo.Revision.Value, Is.EqualTo(1));
            Assert.That(echo0.Echo.State.Value, Is.EqualTo(5));
            Assert.That(echo1.Echo.State.Value, Is.EqualTo(5));
            Assert.That(echo0.Echo.Outcome, Is.EqualTo(ApplyOutcome.Applied));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task GrantedTakeback_ReachesRequesterAndNonRequester()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            var ws1 = await ConnectAsync(session.Session, 1, cts.Token);

            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot
            await ReceiveFrameAsync(ws1, cts.Token); // JoinSnapshot

            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 3, predictedHash: 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // own echo
            await ReceiveFrameAsync(ws1, cts.Token); // broadcast echo

            await SendTakebackAsync(ws0, session.Session, 0, seqAtRequestTime: 1, steps: 1, cts.Token);

            var tb0 = await ReceiveFrameAsync(ws0, cts.Token);
            var tb1 = await ReceiveFrameAsync(ws1, cts.Token);

            Assert.That(tb0.Kind, Is.EqualTo(ServerFrameKind.TakebackBroadcast),
                "requester receives broadcast on granted takeback");
            Assert.That(tb1.Kind, Is.EqualTo(ServerFrameKind.TakebackBroadcast),
                "non-requester receives broadcast on granted takeback");
            Assert.That(tb0.TakebackBroadcast.ForSeat.Value, Is.EqualTo(0));
            Assert.That(tb1.TakebackBroadcast.ForSeat.Value, Is.EqualTo(1));
            Assert.That(tb0.TakebackBroadcast.RequestingSeat.Value, Is.EqualTo(0));
            Assert.That(tb1.TakebackBroadcast.RequestingSeat.Value, Is.EqualTo(0));
            Assert.That(tb0.TakebackBroadcast.StepsRewound, Is.EqualTo(1));
            Assert.That(tb0.TakebackBroadcast.State.Value, Is.EqualTo(0),
                "state rewound to initial 0");
            Assert.That(tb1.TakebackBroadcast.State.Value, Is.EqualTo(0));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task DuplicateSeatAttach_IsRejected()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            // Second attach on seat 0 should be closed by the server.
            var ws0Dup = await ConnectAsync(session.Session, 0, cts.Token);
            // The rejection comes as a PolicyViolation close on the
            // server side; the client reads that as a Close frame.
            var buffer = new byte[1024];
            var res = await ws0Dup.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            Assert.That(res.MessageType, Is.EqualTo(WebSocketMessageType.Close));
            Assert.That(res.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task InvalidSeatIndex_RejectsConnection()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var wsClient = _factory.Server.CreateWebSocketClient();
            var uri = new UriBuilder(_factory.Server.BaseAddress) { Scheme = "ws" };
            uri.Path = $"/session/{session.Session.Value}";
            uri.Query = "seat=99";

            // TestHost.WebSocketClient throws InvalidOperationException on a
            // handshake that returns a non-101 status; the underlying HTTP
            // status is 400 from the seat-range guard.
            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wsClient.ConnectAsync(uri.Uri, cts.Token),
                "out-of-range seat must not upgrade to WebSocket");
            Assert.That(ex.Message, Does.Contain("400"));
        }

        private static async Task SendRawAsync(WebSocket socket, byte[] bytes, CancellationToken ct)
            => await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);

        [Test]
        public async Task SeatMismatchAction_EmitsErrorWithOriginalSeq()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            // Seat 0 sends a frame claiming seat 1 as its submitter — should
            // be rejected with an ErrorEnvelope whose AckedSeq matches the
            // originally-submitted seq, so the client's OnError can retire
            // the matching optimistic entry.
            var frame = new ClientFrame<CounterAction>
            {
                Kind = ClientFrameKind.Action,
                Action = new ActionEnvelope<CounterAction>
                {
                    Session = session.Session,
                    Seat = new SeatId(1),          // wrong seat
                    Seq = new ClientSeq(42),
                    Action = new CounterAction { Delta = 5 },
                    PredictedStateHash = 0,
                },
            };
            await SendRawAsync(ws0, JsonSerializer.SerializeToUtf8Bytes(frame, EnvelopeCodec.Options), cts.Token);

            var received = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(received.Kind, Is.EqualTo(ServerFrameKind.Error));
            Assert.That(received.Error.Code, Is.EqualTo("seat_mismatch"));
            Assert.That(received.Error.AckedSeq.Value, Is.EqualTo(42),
                "ErrorEnvelope.AckedSeq must carry the original seq so the dispatcher can retire the matching optimistic entry");

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task MalformedJsonFrame_EmitsMalformedFrameError()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            // Send bytes that aren't valid JSON at all.
            await SendRawAsync(ws0, Encoding.UTF8.GetBytes("{not json"), cts.Token);

            var received = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(received.Kind, Is.EqualTo(ServerFrameKind.Error),
                "malformed bytes must surface as ErrorEnvelope, not be silently dropped");
            Assert.That(received.Error.Code, Is.EqualTo("malformed_frame"));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task ConcurrentActions_AreSerializedNotRaced()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            var ws1 = await ConnectAsync(session.Session, 1, cts.Token);

            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot
            await ReceiveFrameAsync(ws1, cts.Token); // JoinSnapshot

            // Fire both actions without awaiting between sends. Both go
            // into the session dispatcher's channel; the dispatcher
            // processes them one at a time.
            var sendA = SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 2, predictedHash: 0, cts.Token);
            var sendB = SendActionAsync(ws1, session.Session, 1, seq: 1, delta: 3, predictedHash: 0, cts.Token);
            await Task.WhenAll(sendA, sendB);

            // Each seat will get two echoes (one per action in some order).
            var frames0 = new[] { await ReceiveFrameAsync(ws0, cts.Token), await ReceiveFrameAsync(ws0, cts.Token) };
            var frames1 = new[] { await ReceiveFrameAsync(ws1, cts.Token), await ReceiveFrameAsync(ws1, cts.Token) };

            foreach (var f in frames0) Assert.That(f.Kind, Is.EqualTo(ServerFrameKind.StateEcho));
            foreach (var f in frames1) Assert.That(f.Kind, Is.EqualTo(ServerFrameKind.StateEcho));

            // Revisions must be {1, 2} in some order, never {1, 1} or {2, 2},
            // which would indicate a race on the canonical state.
            var revs0 = new[] { frames0[0].Echo.Revision.Value, frames0[1].Echo.Revision.Value };
            var revs1 = new[] { frames1[0].Echo.Revision.Value, frames1[1].Echo.Revision.Value };
            Array.Sort(revs0);
            Array.Sort(revs1);
            Assert.That(revs0, Is.EqualTo(new long[] { 1, 2 }), "seat 0 revisions must be {1,2}");
            Assert.That(revs1, Is.EqualTo(new long[] { 1, 2 }), "seat 1 revisions must be {1,2}");

            // Final state value seen at revision 2 must be 2+3=5 on both seats.
            long final0 = frames0[0].Echo.Revision.Value == 2 ? frames0[0].Echo.State.Value : frames0[1].Echo.State.Value;
            long final1 = frames1[0].Echo.Revision.Value == 2 ? frames1[0].Echo.State.Value : frames1[1].Echo.State.Value;
            Assert.That(final0, Is.EqualTo(5));
            Assert.That(final1, Is.EqualTo(5));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }
    }
}
