"""Serve the disposable Windows App Installer test feed over loopback."""

import re
import shutil
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit


class FeedHandler(BaseHTTPRequestHandler):
    content_types = {
        ".appinstaller": "application/appinstaller",
        ".msixbundle": "application/msixbundle",
        ".appx": "application/appx",
        ".cer": "application/pkix-cert",
    }

    def do_HEAD(self):
        self.serve(send_body=False)

    def do_GET(self):
        self.serve(send_body=True)

    def serve(self, send_body):
        # Serve only the flat test artifact. Requests for a directory or extra path are invalid.
        name = urlsplit(self.path).path[1:]
        if not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or Path(name).suffix not in self.content_types:
            self.send_error(404)
            return
        path = self.server.directory / name
        if not path.is_file():
            self.send_error(404)
            return
        size = path.stat().st_size
        start, end = 0, size - 1
        requested = self.headers.get("Range")
        if requested:
            match = re.fullmatch(r"bytes=(\d*)-(\d*)", requested)
            if match and any(match.groups()):
                left, right = match.groups()
                start = int(left) if left else max(0, size - int(right))
                end = min(int(right), end) if left and right else end
            else:
                start = size
            if start > end:
                self.send_response(416)
                self.send_header("Content-Range", f"bytes */{size}")
                self.end_headers()
                return
        self.send_response(206 if requested else 200)
        self.send_header("Content-Type", self.content_types[path.suffix])
        self.send_header("Content-Length", str(end - start + 1))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Last-Modified", self.date_time_string(path.stat().st_mtime))
        if requested:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        if send_body:
            with path.open("rb") as source:
                if not requested:
                    shutil.copyfileobj(source, self.wfile)
                    return
                source.seek(start)
                remaining = end - start + 1
                while remaining:
                    chunk = source.read(min(remaining, 65536))
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    remaining -= len(chunk)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), FeedHandler) as server:
        server.directory = Path(sys.argv[1])
        Path(sys.argv[2]).write_text(str(server.server_port), encoding="ascii")
        server.serve_forever()
