import { afterEach, expect, test } from "bun:test";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Field, Input, TextArea } from "../src/components/ui";

afterEach(cleanup);

test("required fields are marked and reject whitespace-only text", () => {
  render(<Field label="Name"><Input required maxLength={200} /></Field>);
  const input = screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement;
  expect(input.parentElement?.textContent).toContain("*");
  expect(input.maxLength).toBe(200);
  fireEvent.change(input, { target: { value: "   " } });
  expect(input.checkValidity()).toBe(false);
  fireEvent.change(input, { target: { value: "Name" } });
  expect(input.checkValidity()).toBe(true);
});

test("required multiline fields reject whitespace and accept content", () => {
  render(<Field label="Reason"><TextArea required maxLength={4000} /></Field>);
  const input = screen.getByRole("textbox", { name: "Reason" }) as HTMLTextAreaElement;
  fireEvent.change(input, { target: { value: " \n " } });
  expect(input.checkValidity()).toBe(false);
  fireEvent.change(input, { target: { value: "Reason" } });
  expect(input.checkValidity()).toBe(true);
});

test("optional fields are not marked required", () => {
  render(<Field label="Scope"><TextArea maxLength={4000} /></Field>);
  expect(screen.getByRole("textbox", { name: "Scope" }).parentElement?.textContent).not.toContain("*");
});
