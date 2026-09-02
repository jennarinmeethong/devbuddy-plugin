import { ApiError } from "../api/client";
import { Alert } from "./ui";

/**
 * A refusal, shown as the server explained it.
 *
 * Never reworded. The server is careful about what a denial says — it names the rule and not the
 * resource it was protecting — and a friendlier message invented here would either lose that care
 * or undo it.
 */
export function Failure({ error }: { error: unknown }) {
  if (error instanceof ApiError) {
    return (
      <Alert tone="error">
        <strong className="font-medium">{error.title}</strong>
        {error.detail ? <span> — {error.detail}</span> : null}
        {error.details.length > 0 ? (
          <ul className="mt-1 list-disc pl-5">
            {error.details.map((detail) => (
              <li key={detail}>{detail}</li>
            ))}
          </ul>
        ) : null}
      </Alert>
    );
  }

  return <Alert tone="error">The server could not be reached.</Alert>;
}
