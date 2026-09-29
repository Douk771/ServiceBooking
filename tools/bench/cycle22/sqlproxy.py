#!/usr/bin/env python3
"""Tiny PostgreSQL wire-protocol counting proxy (no pg_stat_statements on this server).

Usage: sqlproxy.py <listen-port> <control-port>   (upstream: 127.0.0.1:5432, SSL must be disabled)

Counts, per client->server stream: Parse messages (= SQL statements sent by Npgsql, one per statement;
'DISCARD ALL' pool resets counted separately), simple Query messages, Sync (= round trips); per
server->client stream: DataRow messages (= rows returned) and total bytes. GET http://127.0.0.1:<control>/stats
returns the counters (+ the SQL texts seen since the last /reset); GET /reset zeroes them.
"""
import asyncio
import json
import struct
import sys

UP_HOST, UP_PORT = "127.0.0.1", 5432
C = {}


def reset():
    C.update(stmts=0, resets=0, queries=0, syncs=0, rows=0, bytes_from_db=0, bytes_to_db=0, texts=[])


reset()


async def pump_client(reader, writer, state):
    buf = b""
    startup_done = False
    while True:
        data = await reader.read(65536)
        if not data:
            break
        writer.write(data)
        C["bytes_to_db"] += len(data)
        buf += data
        while True:
            if not startup_done:
                if len(buf) < 8:
                    break
                ln, code = struct.unpack("!II", buf[:8])
                if len(buf) < ln:
                    break
                buf = buf[ln:]
                if code in (80877103, 80877104):   # SSLRequest / GSSENCRequest -> server answers 1 byte
                    state["single_bytes"] += 1
                else:
                    startup_done = True
                continue
            if len(buf) < 5:
                break
            t = buf[0:1]
            ln = struct.unpack("!I", buf[1:5])[0]
            if len(buf) < 1 + ln:
                break
            body = buf[5:1 + ln]
            buf = buf[1 + ln:]
            if t == b"P":
                name_end = body.index(b"\0")
                q_end = body.index(b"\0", name_end + 1)
                text = body[name_end + 1:q_end].decode("utf-8", "replace")
                if text.strip().upper().startswith("DISCARD ALL"):
                    C["resets"] += 1
                else:
                    C["stmts"] += 1
                    C["texts"].append(text)
            elif t == b"Q":
                text = body[:-1].decode("utf-8", "replace")
                if text.strip().upper().startswith("DISCARD ALL"):
                    C["resets"] += 1
                else:
                    C["queries"] += 1
                    C["texts"].append(text)
            elif t == b"S":
                C["syncs"] += 1
        await writer.drain()
    writer.close()


async def pump_server(reader, writer, state):
    buf = b""
    while True:
        data = await reader.read(65536)
        if not data:
            break
        writer.write(data)
        C["bytes_from_db"] += len(data)
        buf += data
        while True:
            if state["single_bytes"] > 0:
                if not buf:
                    break
                buf = buf[1:]
                state["single_bytes"] -= 1
                continue
            if len(buf) < 5:
                break
            ln = struct.unpack("!I", buf[1:5])[0]
            if len(buf) < 1 + ln:
                break
            if buf[0:1] == b"D":
                C["rows"] += 1
            buf = buf[1 + ln:]
        await writer.drain()
    writer.close()


async def handle(creader, cwriter):
    sreader, swriter = await asyncio.open_connection(UP_HOST, UP_PORT)
    state = {"single_bytes": 0}
    await asyncio.gather(pump_client(creader, swriter, state), pump_server(sreader, cwriter, state),
                         return_exceptions=True)


async def control(reader, writer):
    line = (await reader.readline()).decode()
    while (await reader.readline()) not in (b"\r\n", b"\n", b""):
        pass
    if "/reset" in line:
        reset()
        body = b"{}"
    else:
        body = json.dumps(C, ensure_ascii=False).encode()
    writer.write(b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n"
                 % len(body) + body)
    await writer.drain()
    writer.close()


async def main():
    port, cport = int(sys.argv[1]), int(sys.argv[2])
    s1 = await asyncio.start_server(handle, "127.0.0.1", port)
    s2 = await asyncio.start_server(control, "127.0.0.1", cport)
    async with s1, s2:
        await asyncio.gather(s1.serve_forever(), s2.serve_forever())


asyncio.run(main())
