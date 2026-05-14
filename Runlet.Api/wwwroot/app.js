const state = {
  runs: [],
  selectedRunId: null,
  selectedDetail: null,
  refreshTimer: null
};

const els = {
  form: document.querySelector("#createRunForm"),
  image: document.querySelector("#imageInput"),
  executionMode: document.querySelector("#executionModeInput"),
  timeout: document.querySelector("#timeoutInput"),
  steps: document.querySelector("#stepsInput"),
  message: document.querySelector("#message"),
  runsList: document.querySelector("#runsList"),
  runCount: document.querySelector("#runCount"),
  runDetail: document.querySelector("#runDetail"),
  cancelButton: document.querySelector("#cancelButton"),
  refreshButton: document.querySelector("#refreshButton")
};

els.form.addEventListener("submit", async (event) => {
  event.preventDefault();
  await createRun();
});

els.refreshButton.addEventListener("click", async () => {
  await refresh();
});

els.cancelButton.addEventListener("click", async () => {
  if (!state.selectedRunId) {
    return;
  }

  await cancelRun(state.selectedRunId);
});

void refresh();
state.refreshTimer = window.setInterval(refresh, 2000);

async function createRun() {
  const steps = els.steps.value
    .split(/\r?\n/)
    .map((step) => step.trim())
    .filter(Boolean);

  const body = {
    image: els.image.value.trim(),
    executionMode: els.executionMode.value,
    stepTimeoutSeconds: Number(els.timeout.value),
    steps
  };

  setMessage("Creating run...");

  try {
    const response = await fetch("/runs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    const run = await response.json();
    state.selectedRunId = run.id;
    setMessage(`Created ${shortId(run.id)}.`);
    await refresh();
  } catch (error) {
    setMessage(error.message || "Could not create run.");
  }
}

async function cancelRun(id) {
  setMessage(`Cancelling ${shortId(id)}...`);

  try {
    const response = await fetch(`/runs/${id}/cancel`, { method: "POST" });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    setMessage(`Cancellation requested for ${shortId(id)}.`);
    await refresh();
  } catch (error) {
    setMessage(error.message || "Could not cancel run.");
  }
}

async function refresh() {
  try {
    const runs = await fetchJson("/runs");
    state.runs = runs;

    if (!state.selectedRunId && runs.length > 0) {
      state.selectedRunId = runs[0].id;
    }

    renderRuns();

    if (state.selectedRunId) {
      state.selectedDetail = await fetchJson(`/runs/${state.selectedRunId}`);
      renderDetail();
    } else {
      state.selectedDetail = null;
      renderDetail();
    }
  } catch (error) {
    setMessage(error.message || "Refresh failed.");
  }
}

function renderRuns() {
  els.runCount.textContent = `${state.runs.length} shown`;
  els.runsList.replaceChildren();

  for (const run of state.runs) {
    const item = document.createElement("button");
    item.type = "button";
    item.className = `run-item${run.id === state.selectedRunId ? " selected" : ""}`;
    item.addEventListener("click", async () => {
      state.selectedRunId = run.id;
      await refresh();
    });

    item.innerHTML = `
      <div class="run-title">
        <span class="run-id">${escapeHtml(shortId(run.id))}</span>
        ${statusBadge(run.status)}
      </div>
      <div class="meta-row">
        <span>${escapeHtml(run.executionMode)}</span>
        <span class="muted">${escapeHtml(run.image)}</span>
      </div>
      <div class="muted">Last heartbeat: ${formatHeartbeat(run)}</div>
      <div class="status-row muted">
        <span>${run.succeededStepCount}/${run.stepCount} ok</span>
        <span>${run.failedStepCount} failed</span>
        <span>${run.skippedStepCount} skipped</span>
        <span>${run.cancelledStepCount} cancelled</span>
      </div>
    `;

    els.runsList.append(item);
  }
}

function renderDetail() {
  const detail = state.selectedDetail;

  if (!detail) {
    els.cancelButton.hidden = true;
    els.runDetail.className = "detail-empty";
    els.runDetail.textContent = "Select a run to inspect it.";
    return;
  }

  const run = detail.run;
  els.cancelButton.hidden = !["Pending", "Running"].includes(run.status);
  els.runDetail.className = "";

  const logs = detail.logs
    .map((log) => `[${formatTime(log.createdAt)}] ${log.message}`)
    .join("\n");

  els.runDetail.innerHTML = `
    <div class="summary-grid">
      ${summaryItem("Status", statusBadge(run.status))}
      ${summaryItem("Executor", escapeHtml(run.executionMode))}
      ${summaryItem("Image", escapeHtml(run.image))}
      ${summaryItem("Timeout", `${run.stepTimeoutSeconds}s`)}
      ${summaryItem("Created", formatDate(run.createdAt))}
      ${summaryItem("Started", formatDate(run.startedAt))}
      ${summaryItem("Completed", formatDate(run.completedAt))}
      ${summaryItem("Cancel requested", formatDate(run.cancellationRequestedAt))}
      ${summaryItem("Last heartbeat", formatHeartbeat(run))}
    </div>

    <h2>Steps</h2>
    <div class="steps">
      ${run.steps.map(renderStep).join("")}
    </div>

    <h2>Logs</h2>
    <pre class="logs">${escapeHtml(logs || "No logs yet.")}</pre>
  `;
}

function renderStep(step) {
  return `
    <div class="step">
      <div class="step-head">
        <strong>Step ${step.order}</strong>
        ${statusBadge(step.status)}
      </div>
      <div class="command">${escapeHtml(step.command)}</div>
      <div class="muted">Exit: ${step.exitCode ?? "-"}</div>
    </div>
  `;
}

function summaryItem(label, value) {
  return `
    <div class="summary-item">
      <span>${escapeHtml(label)}</span>
      <strong>${value}</strong>
    </div>
  `;
}

function statusBadge(status) {
  return `<span class="status ${escapeHtml(status)}">${escapeHtml(status)}</span>`;
}

async function fetchJson(url) {
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json();
}

function setMessage(message) {
  els.message.textContent = message;
}

function shortId(id) {
  return id.slice(0, 8);
}

function formatDate(value) {
  if (!value) {
    return "-";
  }

  return new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(value));
}

function formatTime(value) {
  return formatDate(value);
}

function formatRelative(value) {
  if (!value) {
    return "-";
  }

  const seconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));

  if (seconds < 2) {
    return "just now";
  }

  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.round(seconds / 60);
  return `${minutes}m ago`;
}

function formatHeartbeat(run) {
  if (!run.lastHeartbeatAt) {
    return "-";
  }

  return run.status === "Running"
    ? formatRelative(run.lastHeartbeatAt)
    : formatDate(run.lastHeartbeatAt);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
