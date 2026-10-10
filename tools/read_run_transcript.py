"""Summarise one Claude Code session transcript for measurement runs.

Prints token totals deduplicated by message.id (the JSONL writes one message on several lines, so a naive sum is
about twice too high), the number of assistant messages, the time span, and the flow of text, tool calls and tool
results.

Usage:
    python read_run_transcript.py <session.jsonl>
    python read_run_transcript.py --project <dir under ~/.claude/projects>   # newest session in that folder
"""
import argparse
import glob
import json
import os
import sys


def newest_session(project: str) -> str:
    folder = project if os.path.isdir(project) else os.path.join(os.path.expanduser('~'), '.claude', 'projects', project)
    files = glob.glob(os.path.join(folder, '*.jsonl'))
    if not files:
        sys.exit(f'no .jsonl files in {folder}')
    return max(files, key=os.path.getmtime)


def summarise(path: str) -> None:
    usage_by_message, lines, stamps = {}, [], []
    with open(path, encoding='utf-8') as f:
        for raw in f:
            try:
                entry = json.loads(raw)
            except ValueError:
                continue
            if entry.get('timestamp'):
                stamps.append(entry['timestamp'])
            message = entry.get('message') or {}
            if entry.get('type') == 'assistant':
                usage_by_message[message.get('id')] = message.get('usage') or {}
                for block in message.get('content') or []:
                    if block.get('type') == 'text':
                        lines.append('ASSIST: ' + block['text'][:1500])
                    elif block.get('type') == 'tool_use':
                        lines.append('  TOOL: ' + block['name'] + ' ' + json.dumps(block.get('input'), ensure_ascii=False)[:350])
            elif entry.get('type') == 'user':
                content = message.get('content')
                if isinstance(content, str):
                    lines.append('USER: ' + content[:150])
                    continue
                for block in content or []:
                    if block.get('type') == 'text':
                        lines.append('USER: ' + block['text'][:150])
                    elif block.get('type') == 'tool_result':
                        result = block.get('content')
                        text = result if isinstance(result, str) else ' '.join(
                            part.get('text', '') for part in result or [] if isinstance(part, dict))
                        lines.append('  RESULT(%d lines): ' % (text.count('\n') + 1) + text[:220].replace('\n', ' | '))

    totals = {}
    for usage in usage_by_message.values():
        for key in ('input_tokens', 'cache_creation_input_tokens', 'cache_read_input_tokens', 'output_tokens'):
            totals[key] = totals.get(key, 0) + (usage.get(key) or 0)

    sys.stdout.reconfigure(encoding='utf-8')
    span = f'{stamps[0]} .. {stamps[-1]}' if stamps else 'n/a'
    print(os.path.basename(path), 'assistant messages:', len(usage_by_message), totals, 'span:', span)
    print('\n'.join(lines))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('session', nargs='?', help='path to a session .jsonl file')
    parser.add_argument('--project', help='project folder (full path or name under ~/.claude/projects); newest session is used')
    args = parser.parse_args()
    if not args.session and not args.project:
        parser.error('give a session file or --project')
    summarise(args.session or newest_session(args.project))


if __name__ == '__main__':
    main()
