#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""nd_bridge.py — thin JSON bridge over the `novel-downloader` package.

Commands
  sites                                  -> [{site}]
  search <keyword> [--sites a,b] [--limit N] [--per-site N]
  info <site> <book_id>
  download <site> <book_id> <outdir> [--format txt,epub] [--raw-dir D] [--workers N]

All output is UTF-8 JSON on stdout.  Progress lines look like:
  {"ev":"progress","done":12,"total":300}
"""

from __future__ import annotations

import argparse
import inspect
import asyncio
import json
import os
import sys
import traceback

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def emit(obj) -> None:
    sys.stdout.write(json.dumps(obj, ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


class _UI:
    """Implements novel_downloader.plugins.protocols.DownloadUI."""

    async def on_start(self, book) -> None:
        emit({"ev": "start", "book_id": getattr(book, "book_id", None)})

    async def on_progress(self, done: int, total: int) -> None:
        emit({"ev": "progress", "done": done, "total": total})

    async def on_complete(self, book) -> None:
        emit({"ev": "complete", "book_id": getattr(book, "book_id", None)})


def _client_cfg(raw_dir: str, out_dir: str, workers: int):
    from novel_downloader.schemas.config import ClientConfig

    cfg = ClientConfig()
    cfg.raw_data_dir = raw_dir
    cfg.output_dir = out_dir
    cfg.workers = max(1, int(workers))
    cfg.cache_book_info = True
    cfg.cache_chapter = True
    return cfg


async def _open_client(site: str, raw_dir: str, out_dir: str, workers: int):
    """Build a client and initialise its fetcher/parser (required before any fetch)."""
    from novel_downloader.plugins.registry import registrar

    cfg = _client_cfg(raw_dir, out_dir, workers)
    cli = registrar.get_client(site, cfg)
    await cli.init(cfg.fetcher_cfg, cfg.parser_cfg)
    return cli, cfg


def _book_cfg(book_id: str, start_id=None, end_id=None):
    from novel_downloader.schemas.config import BookConfig

    kwargs = {"book_id": str(book_id)}
    if start_id:
        kwargs["start_id"] = str(start_id)
    if end_id:
        kwargs["end_id"] = str(end_id)
    return BookConfig(**kwargs)


def cmd_sites(_args) -> int:
    # authoritative list: site plugin directories shipped with the package
    import novel_downloader

    base = os.path.join(os.path.dirname(novel_downloader.__file__), "plugins", "sites")
    keys = sorted(
        d
        for d in os.listdir(base)
        if os.path.isdir(os.path.join(base, d)) and not d.startswith("_")
    )
    emit({"sites": keys, "count": len(keys)})
    return 0


async def _search(args) -> int:
    from novel_downloader.plugins.search import search

    sites = [s.strip() for s in args.sites.split(",") if s.strip()] if args.sites else None
    res = await search(
        args.keyword,
        sites=sites,
        limit=args.limit,
        per_site_limit=args.per_site,
        timeout=args.timeout,
    )
    emit({"items": list(res)})
    return 0


def cmd_search(args) -> int:
    return asyncio.run(_search(args))


async def _info(args) -> int:
    raw = args.raw_dir or os.path.join(os.path.dirname(os.path.abspath(__file__)), "_raw")
    cli, _cfg = await _open_client(args.site, raw, raw, 1)
    try:
        info = await cli.get_book_info(book_id=str(args.book_id))
        vols = info.get("volumes") or []
        chapters = 0
        vlist = []
        for v in vols:
            chs = v.get("chapters") or []
            chapters += len(chs)
            vlist.append({"name": v.get("volume_name") or v.get("name") or "", "count": len(chs)})
        emit(
            {
                "ok": True,
                "site": args.site,
                "book_id": str(args.book_id),
                "title": info.get("book_name") or info.get("title") or "",
                "author": info.get("author") or "",
                "cover_url": info.get("cover_url") or info.get("thumb_url") or "",
                "intro": (info.get("intro") or info.get("description") or "")[:800],
                "chapter_count": chapters,
                "volumes": vlist,
            }
        )
    finally:
        try:
            await cli.close()
        except Exception:
            pass
    return 0


def cmd_info(args) -> int:
    return asyncio.run(_info(args))


async def _download(args) -> int:
    out_dir = os.path.abspath(args.outdir)
    raw_dir = os.path.abspath(args.raw_dir or os.path.join(out_dir, "_raw"))
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(raw_dir, exist_ok=True)

    cli, _cfg = await _open_client(args.site, raw_dir, out_dir, args.workers)
    book = _book_cfg(args.book_id, args.start_id, args.end_id)
    try:
        await cli.download_book(book, ui=_UI())
        formats = [f.strip() for f in (args.format or "txt").split(",") if f.strip()]
        res = cli.export(book, formats=formats)
        if inspect.isawaitable(res):
            res = await res
        files = res or {}
        flat = []
        for fmt, paths in (files or {}).items():
            for p in paths or []:
                flat.append({"format": fmt, "path": str(p)})
        emit({"ok": True, "files": flat})
    finally:
        try:
            await cli.close()
        except Exception:
            pass
    return 0


def cmd_download(args) -> int:
    return asyncio.run(_download(args))


def main() -> int:
    ap = argparse.ArgumentParser(prog="nd_bridge")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("sites")
    p.set_defaults(fn=cmd_sites)

    p = sub.add_parser("search")
    p.add_argument("keyword")
    p.add_argument("--sites", default="")
    p.add_argument("--limit", type=int, default=40)
    p.add_argument("--per-site", type=int, default=5, dest="per_site")
    p.add_argument("--timeout", type=float, default=12.0)
    p.set_defaults(fn=cmd_search)

    p = sub.add_parser("info")
    p.add_argument("site")
    p.add_argument("book_id")
    p.add_argument("--raw-dir", default="")
    p.set_defaults(fn=cmd_info)

    p = sub.add_parser("download")
    p.add_argument("site")
    p.add_argument("book_id")
    p.add_argument("outdir")
    p.add_argument("--format", default="txt")
    p.add_argument("--raw-dir", default="")
    p.add_argument("--workers", type=int, default=4)
    p.add_argument("--start-id", default=None)
    p.add_argument("--end-id", default=None)
    p.set_defaults(fn=cmd_download)

    args = ap.parse_args()
    try:
        return args.fn(args)
    except Exception as exc:  # noqa: BLE001
        emit({"ok": False, "error": f"{type(exc).__name__}: {exc}", "trace": traceback.format_exc()[-1500:]})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
