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

        /// Attach without naming a seat — server claims the lowest free one.
        private async Task<WebSocket> ConnectClaimAsync(SessionId session, CancellationToken ct)
        {
            var wsClient = _factory.Server.CreateWebSocketClient();
            var uri = new UriBuilder(_factory.Server.BaseAddress) { Scheme = "ws" };
            uri.Path = $"/session/{session.Value}";
            return await wsClient.ConnectAsync(uri.Uri, ct);
        }

        /// Reattach using a token returned from a prior JoinSnapshot.
        private async Task<WebSocket> ConnectReattachAsync(SessionId session, int seat, string token, CancellationToken ct)
        {
            var wsClient = _factory.Server.CreateWebSocketClient();
            var uri = new UriBuilder(_factory.Server.BaseAddress) { Scheme = "ws" };
            uri.Path = $"/session/{session.Value}";
            uri.Query = $"seat={seat}&reattach={token}";
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

        // P3: server-assigned seat-claim path. Three clients connect without
        // naming a seat — each must land on 0, 1, 2 in connect order. The
        // JoinSnapshot.ForSeat is the only way the client learns which seat
        // it got, so we assert on that field directly.
        [Test]
        public async Task ClaimFreeSeat_AssignsLowestFreeInConnectOrder()
        {
            var session = await OpenAsync(seatCount: 3);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(join0.Kind, Is.EqualTo(ServerFrameKind.JoinSnapshot));
            Assert.That(join0.JoinSnapshot.ForSeat.Value, Is.EqualTo(0));

            var ws1 = await ConnectClaimAsync(session.Session, cts.Token);
            var join1 = await ReceiveFrameAsync(ws1, cts.Token);
            Assert.That(join1.JoinSnapshot.ForSeat.Value, Is.EqualTo(1));

            var ws2 = await ConnectClaimAsync(session.Session, cts.Token);
            var join2 = await ReceiveFrameAsync(ws2, cts.Token);
            Assert.That(join2.JoinSnapshot.ForSeat.Value, Is.EqualTo(2));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws2.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // P5 ownership: once a seat is claimed it stays owned for the
        // remainder of the session. A disconnect does not release
        // ownership — the original client can reattach via token (5c)
        // but a fresh no-token claim from a new client must not steal
        // the seat. Inverts the old "reclaim after disconnect" test
        // which reflected the pre-P5 "seats free on disconnect" shape.
        [Test]
        public async Task OwnedSeat_NotReclaimed_AfterDisconnect()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot seat 0
            var ws1 = await ConnectClaimAsync(session.Session, cts.Token);
            await ReceiveFrameAsync(ws1, cts.Token); // JoinSnapshot seat 1

            // Drop seat 0. The dispatcher processes the detach asynchronously —
            // before detach runs, _seats still holds 0 and the claim scan
            // skips it; after detach runs, _seatOwners holds 0 and the
            // scan still skips it. Either way, a fresh claim must see
            // session_full.
            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);

            var ws2 = await ConnectClaimAsync(session.Session, cts.Token);
            var buffer = new byte[1024];
            var res = await ws2.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            Assert.That(res.MessageType, Is.EqualTo(WebSocketMessageType.Close),
                "fresh claim after ownership-persisting disconnect must be rejected");
            Assert.That(res.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            Assert.That(res.CloseStatusDescription, Is.EqualTo("session_full"));

            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // P5 5c: valid token on a disconnected owned seat must succeed
        // and echo the SAME token — token identity survives reattach so
        // the client can reconnect repeatedly without rotating its stash.
        [Test]
        public async Task Reattach_ValidToken_ResumesSeatWithSameToken()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            var token = join0.JoinSnapshot.ReconnectToken;
            int seat = join0.JoinSnapshot.ForSeat.Value;
            Assert.That(token, Is.Not.Null.And.Not.Empty);

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);

            var ws0Re = await ConnectReattachAsync(session.Session, seat, token, cts.Token);
            var reJoin = await ReceiveFrameAsync(ws0Re, cts.Token);
            Assert.That(reJoin.Kind, Is.EqualTo(ServerFrameKind.JoinSnapshot));
            Assert.That(reJoin.JoinSnapshot.ForSeat.Value, Is.EqualTo(seat),
                "reattach must restore the original seat index");
            Assert.That(reJoin.JoinSnapshot.ReconnectToken, Is.EqualTo(token),
                "reattach must echo the same token — identity is stable across reconnects");

            await ws0Re.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // P5 5c: wrong token must be rejected with PolicyViolation — a
        // new client guessing or scraping can't hijack an owned seat.
        [Test]
        public async Task Reattach_WrongToken_RejectsWithTokenMismatch()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            int seat = join0.JoinSnapshot.ForSeat.Value;
            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);

            var wsBad = await ConnectReattachAsync(session.Session, seat, "not-the-real-token", cts.Token);
            var buffer = new byte[1024];
            var res = await wsBad.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            Assert.That(res.MessageType, Is.EqualTo(WebSocketMessageType.Close));
            Assert.That(res.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            Assert.That(res.CloseStatusDescription, Is.EqualTo("token_mismatch"));
        }

        // P5 5c: reattach against a seat nobody has ever claimed (owner
        // entry absent) must also be rejected as token_mismatch — the
        // client should claim fresh instead. Exercises the "unknown
        // seat" branch of HandleReattach.
        [Test]
        public async Task Reattach_UnclaimedSeat_RejectsWithTokenMismatch()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Seat 1 never claimed — reattach against it should fail.
            var wsBad = await ConnectReattachAsync(session.Session, 1, "any-token", cts.Token);
            var buffer = new byte[1024];
            var res = await wsBad.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            Assert.That(res.MessageType, Is.EqualTo(WebSocketMessageType.Close));
            Assert.That(res.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            Assert.That(res.CloseStatusDescription, Is.EqualTo("token_mismatch"));
        }

        // P5 5c: after one seat reattaches, the other seat's slot
        // should still be claimable as a fresh seat (roster isn't
        // corrupted by the reattach path).
        [Test]
        public async Task Reattach_DoesNotBlockFreshClaimOnOtherSeats()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            var token = join0.JoinSnapshot.ReconnectToken;
            int seat0 = join0.JoinSnapshot.ForSeat.Value;
            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);

            var ws0Re = await ConnectReattachAsync(session.Session, seat0, token, cts.Token);
            var reJoin = await ReceiveFrameAsync(ws0Re, cts.Token);
            Assert.That(reJoin.JoinSnapshot.ForSeat.Value, Is.EqualTo(seat0));

            var ws1 = await ConnectClaimAsync(session.Session, cts.Token);
            var join1 = await ReceiveFrameAsync(ws1, cts.Token);
            Assert.That(join1.JoinSnapshot.ForSeat.Value, Is.EqualTo(1 - seat0),
                "the other seat should be the one freshly claimed");

            await ws0Re.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // P5 5b: every JoinSnapshot issued on a fresh claim must carry a
        // non-null reconnect token so the client can stash it for a
        // later reattach. Two distinct seats must receive distinct
        // tokens — sharing a token across seats would let a reconnect
        // on seat A revive seat B.
        [Test]
        public async Task ClaimFreeSeat_IssuesDistinctReconnectTokens()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            var join0 = await ReceiveFrameAsync(ws0, cts.Token);
            var ws1 = await ConnectClaimAsync(session.Session, cts.Token);
            var join1 = await ReceiveFrameAsync(ws1, cts.Token);

            Assert.That(join0.JoinSnapshot.ReconnectToken, Is.Not.Null.And.Not.Empty,
                "seat 0 must get a token");
            Assert.That(join1.JoinSnapshot.ReconnectToken, Is.Not.Null.And.Not.Empty,
                "seat 1 must get a token");
            Assert.That(join0.JoinSnapshot.ReconnectToken,
                Is.Not.EqualTo(join1.JoinSnapshot.ReconnectToken),
                "tokens must be seat-specific");

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // Session_full: third claim on a 2-seat session should get a
        // PolicyViolation close with the "session_full" reason. The
        // client reads that as a Close frame just like duplicate-attach.
        [Test]
        public async Task ClaimFreeSeat_AllTaken_RejectsWithSessionFull()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectClaimAsync(session.Session, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot seat 0
            var ws1 = await ConnectClaimAsync(session.Session, cts.Token);
            await ReceiveFrameAsync(ws1, cts.Token); // JoinSnapshot seat 1

            var ws2 = await ConnectClaimAsync(session.Session, cts.Token);
            var buffer = new byte[1024];
            var res = await ws2.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            Assert.That(res.MessageType, Is.EqualTo(WebSocketMessageType.Close),
                "3rd claim on a 2-seat session must be closed by server");
            Assert.That(res.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            Assert.That(res.CloseStatusDescription, Is.EqualTo("session_full"));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // ClientSeq idempotency: a retransmit with an already-consumed
        // seq must not mutate state twice. The server bounces the
        // duplicate as an ErrorEnvelope(code=duplicate_seq, AckedSeq=dup)
        // so the client's dispatcher can retire the retransmitted
        // optimistic entry; the canonical revision stays put.
        [Test]
        public async Task DuplicateSeq_RejectedWithoutMutatingState()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 4, predictedHash: 0, cts.Token);
            var firstEcho = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(firstEcho.Kind, Is.EqualTo(ServerFrameKind.StateEcho));
            Assert.That(firstEcho.Echo.Revision.Value, Is.EqualTo(1));
            Assert.That(firstEcho.Echo.State.Value, Is.EqualTo(4));

            // Resend the identical envelope (same seq, same delta).
            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 4, predictedHash: 0, cts.Token);
            var duplicateResp = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(duplicateResp.Kind, Is.EqualTo(ServerFrameKind.Error));
            Assert.That(duplicateResp.Error.Code, Is.EqualTo("duplicate_seq"));
            Assert.That(duplicateResp.Error.AckedSeq.Value, Is.EqualTo(1),
                "duplicate response must carry the duplicate seq as AckedSeq so the client can retire its optimistic entry");

            // Confirm state didn't mutate by issuing a fresh seq=2.
            await SendActionAsync(ws0, session.Session, 0, seq: 2, delta: 1, predictedHash: 0, cts.Token);
            var third = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(third.Kind, Is.EqualTo(ServerFrameKind.StateEcho));
            Assert.That(third.Echo.Revision.Value, Is.EqualTo(2),
                "revision only advances for the duplicate-seq case by 0; seq=2 brings it to 2 total");
            Assert.That(third.Echo.State.Value, Is.EqualTo(5),
                "state = 4 (from seq=1) + 1 (from seq=2); the duplicate did not add another 4");

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // Stale-seq dedup: a seq strictly lower than the last-applied one
        // is treated the same as an exact duplicate — both indicate the
        // client's dispatcher got out of sync with the canonical counter.
        [Test]
        public async Task StaleSeq_BelowLastApplied_RejectedAsDuplicate()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            await SendActionAsync(ws0, session.Session, 0, seq: 3, delta: 2, predictedHash: 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // echo for seq=3

            await SendActionAsync(ws0, session.Session, 0, seq: 2, delta: 2, predictedHash: 0, cts.Token);
            var stale = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(stale.Kind, Is.EqualTo(ServerFrameKind.Error));
            Assert.That(stale.Error.Code, Is.EqualTo("duplicate_seq"));
            Assert.That(stale.Error.AckedSeq.Value, Is.EqualTo(2));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // Per-seat scoping: seat 0 and seat 1 maintain independent seq
        // counters. A seq=1 submission from seat 1 must not be rejected
        // just because seat 0 has already submitted seq=1.
        [Test]
        public async Task DuplicateSeq_IsPerSeat_NotPerSession()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            var ws1 = await ConnectAsync(session.Session, 1, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot
            await ReceiveFrameAsync(ws1, cts.Token); // JoinSnapshot

            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 2, predictedHash: 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // own echo
            await ReceiveFrameAsync(ws1, cts.Token); // broadcast

            // Seat 1 uses seq=1 — must be accepted, not deduped.
            await SendActionAsync(ws1, session.Session, 1, seq: 1, delta: 3, predictedHash: 0, cts.Token);
            var seat1Echo = await ReceiveFrameAsync(ws1, cts.Token);
            Assert.That(seat1Echo.Kind, Is.EqualTo(ServerFrameKind.StateEcho),
                "seq counters are per-seat; seat 1's seq=1 must not collide with seat 0's");
            Assert.That(seat1Echo.Echo.Revision.Value, Is.EqualTo(2));
            Assert.That(seat1Echo.Echo.State.Value, Is.EqualTo(5));
            await ReceiveFrameAsync(ws0, cts.Token); // broadcast to seat 0

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        // GET /session/{id}/state — read-only snapshot endpoint. Must
        // reflect current canonical state, route through the dispatcher
        // so it never races an in-flight Apply, and stay consistent with
        // what attached sockets see via StateEcho.
        [Test]
        public async Task GetSessionState_ReflectsCanonicalRevisionAndHash()
        {
            var session = await OpenAsync(seatCount: 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ws0 = await ConnectAsync(session.Session, 0, cts.Token);
            await ReceiveFrameAsync(ws0, cts.Token); // JoinSnapshot

            await SendActionAsync(ws0, session.Session, 0, seq: 1, delta: 7, predictedHash: 0, cts.Token);
            var echo = await ReceiveFrameAsync(ws0, cts.Token);
            Assert.That(echo.Kind, Is.EqualTo(ServerFrameKind.StateEcho));

            using var client = _factory.CreateClient();
            var resp = await client.GetAsync($"/session/{session.Session.Value}/state");
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            Assert.That(root.GetProperty("session").GetString(), Is.EqualTo(session.Session.Value));
            Assert.That(root.GetProperty("gameId").GetString(), Is.EqualTo("counter"));
            Assert.That(root.GetProperty("seatCount").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("seat").GetInt32(), Is.EqualTo(0));
            Assert.That(root.GetProperty("revision").GetInt64(), Is.EqualTo(echo.Echo.Revision.Value));
            Assert.That(root.GetProperty("stateHash").GetInt64(), Is.EqualTo(echo.Echo.StateHash));
            Assert.That(root.GetProperty("state").GetProperty("value").GetInt32(), Is.EqualTo(7));

            await ws0.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }

        [Test]
        public async Task GetSessionState_UnknownSession_Returns404()
        {
            using var client = _factory.CreateClient();
            var resp = await client.GetAsync("/session/no-such-session/state");
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task GetSessionState_SeatOutOfRange_Returns400()
        {
            var session = await OpenAsync(seatCount: 2);
            using var client = _factory.CreateClient();
            var resp = await client.GetAsync($"/session/{session.Session.Value}/state?seat=5");
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        // Parallel claims must not double-assign. All 4 connects happen
        // before any one reads its JoinSnapshot. Every seat in [0..3] must
        // appear exactly once across the four ForSeat values.
        [Test]
        public async Task ClaimFreeSeat_ParallelClaims_NeverDoubleAssign()
        {
            var session = await OpenAsync(seatCount: 4);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var connectTasks = new Task<WebSocket>[4];
            for (int i = 0; i < 4; i++) connectTasks[i] = ConnectClaimAsync(session.Session, cts.Token);
            var sockets = await Task.WhenAll(connectTasks);

            var joinTasks = new Task<ServerFrame<CounterState>>[4];
            for (int i = 0; i < 4; i++) joinTasks[i] = ReceiveFrameAsync(sockets[i], cts.Token);
            var joins = await Task.WhenAll(joinTasks);

            var seats = new System.Collections.Generic.HashSet<int>();
            foreach (var j in joins)
            {
                Assert.That(j.Kind, Is.EqualTo(ServerFrameKind.JoinSnapshot));
                Assert.That(seats.Add(j.JoinSnapshot.ForSeat.Value),
                    $"seat {j.JoinSnapshot.ForSeat.Value} assigned twice");
            }
            Assert.That(seats, Is.EquivalentTo(new[] { 0, 1, 2, 3 }),
                "every seat must be claimed exactly once");

            foreach (var ws in sockets)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        }
    }
}
