#!/usr/bin/env python3
"""검증된 한국어 문서를 Wiki에 게시합니다. 기본 실행은 원격 변경 없는 미리보기입니다."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Mapping

REPOSITORY = "mete0rfish/BrowserDock"
WIKI_URL = f"https://github.com/{REPOSITORY}.wiki.git"
START = "<!-- browserdock-ko-navigation:start -->"
END = "<!-- browserdock-ko-navigation:end -->"
SHARED = {"Home.md", "_Sidebar.md", "_Footer.md"}


def git(work: Path, *args: str, capture: bool = False) -> str:
    result = subprocess.run(
        ["git", "-C", str(work), *args], check=True, text=True,
        encoding="utf-8", stdout=subprocess.PIPE if capture else None,
        timeout=180,
    )
    return result.stdout.strip() if capture else ""


def load_pages(pages: Path) -> dict[str, str]:
    manifest = json.loads((pages.parent / "wiki-manifest.json").read_text(encoding="utf-8"))
    if manifest.get("repository") != REPOSITORY:
        raise ValueError("Manifest의 대상 저장소가 다릅니다.")
    entries = manifest.get("pages")
    if not isinstance(entries, dict) or len(entries) != 20 or manifest.get("page_count") != 20:
        raise ValueError("정확히 20개 문서의 Manifest가 필요합니다.")
    if set(entries) != {p.name for p in pages.glob("*.md")}:
        raise ValueError("Manifest와 페이지 목록이 다릅니다.")
    result: dict[str, str] = {}
    for name, expected in sorted(entries.items()):
        if Path(name).name != name or not name.endswith(".md") or "\\" in name:
            raise ValueError("잘못된 파일명입니다.")
        path = pages / name
        if path.is_symlink():
            raise ValueError(f"심볼릭 링크는 지원하지 않습니다: {name}")
        text = path.read_text(encoding="utf-8")
        text = re.sub(r"\]\(((?:ko-[A-Za-z0-9-]+|Home|_Sidebar|_Footer))\.md\)", r"](\1)", text)
        raw = text.encode("utf-8")
        if hashlib.sha256(raw).hexdigest() != expected:
            raise ValueError(f"문서가 Manifest와 다릅니다: {name}")
        result[name] = raw.decode("utf-8")
    return result


def plan_pages(work: Path, contents: Mapping[str, str]) -> dict[str, str]:
    """충돌을 모두 확인한 뒤 쓸 내용만 반환합니다. 기존 페이지는 삭제하지 않습니다."""
    planned: dict[str, str] = {}
    for name, new in contents.items():
        target = work / name
        if target.is_symlink() or (target.exists() and not target.is_file()):
            raise ValueError(f"일반 파일이 아닌 기존 페이지입니다: {name}")
        alternatives = [p for p in work.iterdir()
                        if p.name != name and p.stem.casefold() == target.stem.casefold()]
        if alternatives:
            raise ValueError(f"같은 제목의 다른 형식/대소문자 페이지가 있습니다: {name}")
        if not target.exists():
            planned[name] = new
            continue
        old = target.read_text(encoding="utf-8")
        if old == new:
            continue
        if name not in SHARED:
            raise ValueError(f"기존 페이지와 내용이 다릅니다. 덮어쓰지 않습니다: {name}")
        if START in old or END in old:
            if old.count(START) != 1 or old.count(END) != 1 or old.index(START) > old.index(END):
                raise ValueError(f"탐색 링크 구간 표식이 올바르지 않습니다: {name}")
            continue
        addition = ("## 한국어 학습 문서\n\n[학습 가이드와 전체 목차](ko-Home)\n"
                    if name == "Home.md" else new)
        planned[name] = old.rstrip() + "\n\n" + START + "\n" + addition.rstrip() + "\n" + END + "\n"
    return planned


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pages", type=Path, default=Path(__file__).resolve().parents[1] / "docs" / "wiki")
    parser.add_argument("--check-only", action="store_true", help="파일 해시만 검사합니다. 네트워크에 연결하지 않습니다.")
    parser.add_argument("--publish", action="store_true", help="검토한 내용을 실제 commit/push합니다.")
    args = parser.parse_args()
    if args.check_only and args.publish:
        parser.error("--check-only와 --publish는 동시에 사용할 수 없습니다.")
    contents = load_pages(args.pages.resolve())
    print(f"문서 20개 해시 검증 완료: {REPOSITORY}")
    if args.check_only:
        return
    with tempfile.TemporaryDirectory(prefix="browserdock-wiki-") as temp:
        work = Path(temp) / "wiki"
        try:
            subprocess.run(["git", "clone", "--depth=1", WIKI_URL, str(work)], check=True, timeout=180)
        except subprocess.CalledProcessError as error:
            raise ValueError("Wiki clone 실패. 초기 Home 페이지와 계정의 Wiki 접근 권한을 확인하세요. 원격 페이지를 변경하지 않았습니다.") from error
        branch = git(work, "symbolic-ref", "--short", "HEAD", capture=True)
        planned = plan_pages(work, contents)
        for name, text in planned.items():
            (work / name).write_bytes(text.encode("utf-8"))
        if not planned:
            print("추가할 변경이 없습니다. 기존 한국어 페이지는 현재 자료와 같습니다.")
            return
        git(work, "add", "--", *sorted(planned))
        git(work, "diff", "--cached", "--stat")
        if not args.publish:
            print("미리보기만 수행했습니다. 원격 변경은 없습니다. 실제 게시에는 --publish를 지정하세요.")
            return
        # 커밋은 사용자의 기존 Git 신원을 사용합니다. 설정이 없으면 Git이 중단합니다.
        git(work, "commit", "-m", "docs: add Korean BrowserDock learning wiki")
        git(work, "push", "origin", f"HEAD:refs/heads/{branch}")
        local = git(work, "rev-parse", "HEAD", capture=True)
        remote = git(work, "ls-remote", "origin", f"refs/heads/{branch}", capture=True).split()
        if not remote or remote[0] != local:
            raise ValueError("원격 커밋이 달라 게시 상태를 확인할 수 없습니다. 강제 push하지 마세요.")
        git(work, "fetch", "--depth=1", "origin", branch)
        git(work, "diff", "--exit-code", "HEAD", "FETCH_HEAD", "--", *sorted(contents))
        print("게시 및 원격 파일 확인 완료. Wiki commit: " + local)
        print(f"https://github.com/{REPOSITORY}/wiki/ko-Home")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"중단: {error}", file=sys.stderr)
        sys.exit(1)
