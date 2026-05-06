#!/usr/bin/env python3
"""
Convert Super 3-D Noah's Ark quiz data from QUESTION.ASM into the XML <Questions>
section used by N3D.xml.

QUESTION.ASM stores each quiz entry in the original DOS data layout used by
WL_QUIZ.C: a null-terminated question string, a null-terminated Bible reference,
then 2-4 answer strings terminated by flag bytes whose low bit marks the correct
answer and whose values 3/4 end the list. This script parses that assembler data
directly and emits the equivalent XML so the questions do not need to be
transcribed by hand.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape


def fail(message: str) -> "None":
	print(f"Error: {message}", file=sys.stderr)
	raise SystemExit(1)


def xml_attr(value: str) -> str:
	return escape(value, {'"': "&quot;", "'": "&apos;"}).replace("\n", "&#10;")


def xml_text(value: str) -> str:
	return escape(value, {"'": "&apos;"}).replace("\n", "&#10;")


def parse_db_payload(payload: str) -> list[int]:
	tokens: list[int] = []
	i = 0

	while i < len(payload):
		ch = payload[i]
		if ch in " \t\r\n,":
			i += 1
			continue
		if ch == ";":
			break
		if ch == '"':
			i += 1
			start = i
			while i < len(payload) and payload[i] != '"':
				i += 1
			if i >= len(payload):
				fail(f"Unterminated string in payload: {payload.rstrip()}")
			tokens.extend(ord(c) for c in payload[start:i])
			i += 1
			continue

		start = i
		while i < len(payload) and payload[i] not in ",;":
			i += 1
		raw = payload[start:i].strip()
		if not raw:
			continue
		if raw.lower().startswith("0x"):
			tokens.append(int(raw, 16))
		elif raw.isdigit():
			tokens.append(int(raw, 10))
		else:
			fail(f"Unsupported token '{raw}' in payload: {payload.rstrip()}")

	return tokens


def decode_c_string(data: list[int], start: int) -> tuple[str, int]:
	chars: list[str] = []
	i = start
	while i < len(data):
		value = data[i]
		i += 1
		if value == 0:
			return "".join(chars), i
		chars.append("\n" if value == 10 else chr(value))
	fail("Missing null terminator in question block")


def decode_answers(data: list[int], start: int) -> list[tuple[str, bool]]:
	answers: list[tuple[str, bool]] = []
	i = start

	while i < len(data):
		chars: list[str] = []
		while i < len(data) and data[i] > 4:
			chars.append("\n" if data[i] == 10 else chr(data[i]))
			i += 1

		if i >= len(data):
			fail("Answer list ended without a terminator flag")

		flag = data[i]
		i += 1
		answers.append(("".join(chars), (flag & 1) == 1))

		if flag >= 3:
			if i != len(data):
				fail("Trailing bytes found after final answer flag")
			break

	if not 2 <= len(answers) <= 4:
		fail(f"Expected 2-4 answers, found {len(answers)}")
	if sum(1 for _, is_correct in answers if is_correct) != 1:
		fail("Expected exactly one correct answer")
	return answers


def main() -> int:
	if len(sys.argv) != 2:
		print("Usage: convert_bible_questions.py <QUESTION.ASM>", file=sys.stderr)
		return 1

	input_path = Path(sys.argv[1])
	if not input_path.is_file():
		fail(f"Input file '{input_path}' not found.")

	lines = input_path.read_text(encoding="cp1252").splitlines()

	pointer_order: list[str] = []
	blocks: dict[str, list[int]] = {}
	current_label: str | None = None

	pointer_re = re.compile(r"^(?:Question\s+)?dd\s+(Quiz\d{3}|Quiz999)\b")
	label_db_re = re.compile(r"^(Quiz\d{3}|Quiz999)\s+db\s+(.*)$")
	cont_db_re = re.compile(r"^\s*db\s+(.*)$")

	for line in lines:
		pointer_match = pointer_re.match(line.strip())
		if pointer_match:
			label = pointer_match.group(1)
			if label != "Quiz999":
				pointer_order.append(label)
			continue

		label_match = label_db_re.match(line.strip())
		if label_match:
			current_label = label_match.group(1)
			blocks.setdefault(current_label, []).extend(parse_db_payload(label_match.group(2)))
			continue

		cont_match = cont_db_re.match(line)
		if cont_match and current_label is not None:
			blocks.setdefault(current_label, []).extend(parse_db_payload(cont_match.group(1)))

	if len(pointer_order) != 99:
		fail(f"Expected 99 questions in pointer table, found {len(pointer_order)}")

	print("<Questions>")
	for label in pointer_order:
		if label not in blocks:
			fail(f"Missing data block for {label}")

		question_text, index = decode_c_string(blocks[label], 0)
		ref_text, index = decode_c_string(blocks[label], index)
		answers = decode_answers(blocks[label], index)

		print(f'\t<Question Text="{xml_attr(question_text)}" Ref="{xml_attr(ref_text)}">')
		for answer_text, is_correct in answers:
			if is_correct:
				print(f"\t\t<Answer Correct=\"true\">{xml_text(answer_text)}</Answer>")
			else:
				print(f"\t\t<Answer>{xml_text(answer_text)}</Answer>")
		print("\t</Question>")
	print("</Questions>")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
