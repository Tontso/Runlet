const state = {
  runs: [],
  statusFilter: "All",
  searchText: "",
  searchTimer: null,
  selectedRunId: null,
  selectedDetail: null,
  refreshTimer: null
};

const staleHeartbeatSeconds = 20;

const els = {
  form: document.querySelector("#createRunForm"),
  name: document.querySelector("#nameInput"),
  image: document.querySelector("#imageInput"),
  executionMode: document.querySelector("#executionModeInput"),
  timeout: document.querySelector("#timeoutInput"),
  maxRetries: document.querySelector("#maxRetriesInput"),
  steps: document.querySelector("#stepsInput"),
  message: document.querySelector("#message"),
  runSearch: document.querySelector("#runSearchInput"),
  statusFilter: document.querySelector("#statusFilterInput"),
  runsList: document.querySelector("#runsList"),
  runCount: document.querySelector("#runCount"),
  runDetail: document.querySelector("#runDetail"),
  rerunButton: document.querySelector("#rerunButton"),
  cancelButton: document.querySelector("#cancelButton"),
  failButton: document.querySelector("#failButton"),
  refreshButton: document.querySelector("#refreshButton")
};

els.form.addEventListener("submit", async (event) => {
  event.preventDefault();
  await createRun();
});

els.refreshButton.addEventListener("click", async () => {
  await refresh();
});

els.statusFilter.addEventListener("change", async () => {
  state.statusFilter = els.statusFilter.value;
  await refresh();
});

els.runSearch.addEventListener("input", async () => {
  state.searchText = els.runSearch.value.trim().toLowerCase();
  window.clearTimeout(state.searchTimer);
  state.searchTimer = window.setTimeout(refresh, 250);
});

els.cancelButton.addEventListener("click", async () => {
  if (!state.selectedRunId) {
    return;
  }

  await cancelRun(state.selectedRunId);
});

els.rerunButton.addEventListener("click", async () => {
  if (!state.selectedRunId) {
    return;
  }

  await rerunRun(state.selectedRunId);
});

els.failButton.addEventListener("click", async () => {
  if (!state.selectedRunId) {
    return;
  }

  await failRun(state.selectedRunId);
});

void refresh();
state.refreshTimer = window.setInterval(refresh, 2000);

async function createRun() {
  const steps = els.steps.value
    .split(/\r?\n/)
    .map((step) => step.trim())
    .filter(Boolean);

  const body = {
    name: els.name.value.trim() || null,
    image: els.image.value.trim(),
    executionMode: els.executionMode.value,
    stepTimeoutSeconds: Number(els.timeout.value),
    maxRetries: Number(els.maxRetries.value),
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
    setMessage(`Created ${displayRunName(run)}.`);
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

async function rerunRun(id) {
  setMessage(`Rerunning ${shortId(id)}...`);

  try {
    const response = await fetch(`/runs/${id}/rerun`, { method: "POST" });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    const run = await response.json();
    state.selectedRunId = run.id;
    setMessage(`Created rerun ${displayRunName(run)}.`);
    await refresh();
  } catch (error) {
    setMessage(error.message || "Could not rerun workflow.");
  }
}

async function failRun(id) {
  setMessage(`Marking ${shortId(id)} failed...`);

  try {
    const response = await fetch(`/runs/${id}/fail`, { method: "POST" });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    setMessage(`Marked ${shortId(id)} failed.`);
    await refresh();
  } catch (error) {
    setMessage(error.message || "Could not mark run failed.");
  }
}

async function refresh() {
  try {
    const runs = await fetchJson(buildRunsUrl());
    state.runs = runs;
    await applyRunFilters();
  } catch (error) {
    setMessage(error.message || "Refresh failed.");
  }
}

async function applyRunFilters() {
  const filteredRuns = getFilteredRuns();
  const selectedRunStillVisible = filteredRuns.some((run) => run.id === state.selectedRunId);

  if (!selectedRunStillVisible) {
    state.selectedRunId = filteredRuns[0]?.id ?? null;
  }

  renderRuns();

  if (state.selectedRunId) {
    state.selectedDetail = await fetchJson(`/runs/${state.selectedRunId}`);
    renderDetail();
  } else {
    state.selectedDetail = null;
    renderDetail();
  }
}

function renderRuns() {
  const filteredRuns = getFilteredRuns();
  els.runCount.textContent = `${filteredRuns.length} shown`;
  els.runsList.replaceChildren();

  if (filteredRuns.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-list";
    empty.textContent = "No runs match these filters.";
    els.runsList.append(empty);
    return;
  }

  for (const run of filteredRuns) {
    const item = document.createElement("button");
    item.type = "button";
    item.className = `run-item${run.id === state.selectedRunId ? " selected" : ""}`;
    item.addEventListener("click", async () => {
      state.selectedRunId = run.id;
      await refresh();
    });

    item.innerHTML = `
      <div class="run-title">
        <span class="run-id">${escapeHtml(displayRunName(run))}</span>
        <span class="badges">${statusBadge(run.status)}${staleBadge(run)}</span>
      </div>
      <div class="muted">ID ${escapeHtml(shortId(run.id))}</div>
      <div class="meta-row">
        <span>${escapeHtml(run.executionMode)}</span>
        <span class="muted">${escapeHtml(run.image)}</span>
      </div>
      <div class="muted">Last heartbeat: ${formatHeartbeat(run)}</div>
      <div class="status-row muted">
        <span>Runtime ${formatDuration(run.startedAt, run.completedAt, run.status)}</span>
        <span>${run.succeededStepCount}/${run.stepCount} ok</span>
        <span>${run.failedStepCount} failed</span>
        <span>${run.skippedStepCount} skipped</span>
        <span>${run.cancelledStepCount} cancelled</span>
      </div>
    `;

    els.runsList.append(item);
  }
}

function getFilteredRuns() {
  return state.runs;
}

function renderDetail() {
  const detail = state.selectedDetail;

  if (!detail) {
    els.rerunButton.hidden = true;
    els.cancelButton.hidden = true;
    els.failButton.hidden = true;
    els.runDetail.className = "detail-empty";
    els.runDetail.textContent = "Select a run to inspect it.";
    return;
  }

  const run = detail.run;
  els.rerunButton.hidden = !["Succeeded", "Failed", "Cancelled"].includes(run.status);
  els.cancelButton.hidden = !["Pending", "Running"].includes(run.status);
  els.failButton.hidden = !isHeartbeatStale(run);
  els.runDetail.className = "";

  const logs = detail.logs.map(renderLog).join("");

  els.runDetail.innerHTML = `
    <div class="summary-grid">
      ${summaryItem("Name", escapeHtml(displayRunName(run)))}
      ${summaryItem("Status", `${statusBadge(run.status)}${staleBadge(run)}`)}
      ${summaryItem("ID", escapeHtml(shortId(run.id)))}
      ${summaryItem("Executor", escapeHtml(run.executionMode))}
      ${summaryItem("Image", escapeHtml(run.image))}
      ${summaryItem("Timeout", `${run.stepTimeoutSeconds}s`)}
      ${summaryItem("Retries", run.maxRetries)}
      ${summaryItem("Created", formatDate(run.createdAt))}
      ${summaryItem("Started", formatDate(run.startedAt))}
      ${summaryItem("Completed", formatDate(run.completedAt))}
      ${summaryItem("Cancel requested", formatDate(run.cancellationRequestedAt))}
      ${summaryItem("Last heartbeat", formatHeartbeat(run))}
      ${summaryItem("Duration", formatDuration(run.startedAt, run.completedAt, run.status))}
    </div>

    <h2>Steps</h2>
    <div class="steps">
      ${run.steps.map(renderStep).join("")}
    </div>

    <h2>Logs</h2>
    <div class="logs">${logs || '<div class="log-line muted">No logs yet.</div>'}</div>
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
      <div class="step-meta muted">
        <span>Exit: ${step.exitCode ?? "-"}</span>
        <span>Attempts: ${step.attemptCount}</span>
        <span>Duration: ${formatDuration(step.startedAt, step.completedAt, step.status)}</span>
      </div>
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

function renderLog(log) {
  return `
    <div class="log-line">
      <span class="log-time">${escapeHtml(formatTime(log.createdAt))}</span>
      <span class="log-kind ${escapeHtml(log.kind)}">${escapeHtml(log.kind)}</span>
      <span class="log-message">${escapeHtml(log.message)}</span>
    </div>
  `;
}

function staleBadge(run) {
  return isHeartbeatStale(run)
    ? '<span class="status Stale">Stale</span>'
    : "";
}

async function fetchJson(url) {
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json();
}

function buildRunsUrl() {
  const query = new URLSearchParams();

  if (state.statusFilter !== "All") {
    query.set("status", state.statusFilter);
  }

  if (state.searchText) {
    query.set("search", state.searchText);
  }

  const queryString = query.toString();
  return queryString ? `/runs?${queryString}` : "/runs";
}

function setMessage(message) {
  els.message.textContent = message;
}

function shortId(id) {
  return id.slice(0, 8);
}

function displayRunName(run) {
  return run.name?.trim() || shortId(run.id);
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
    return run.status === "Running" ? "missing" : "-";
  }

  const heartbeat = run.status === "Running"
    ? formatRelative(run.lastHeartbeatAt)
    : formatDate(run.lastHeartbeatAt);

  return isHeartbeatStale(run) ? `${heartbeat} stale` : heartbeat;
}

function isHeartbeatStale(run) {
  if (run.status !== "Running" || !run.lastHeartbeatAt) {
    return false;
  }

  return secondsSince(run.lastHeartbeatAt) > staleHeartbeatSeconds;
}

function secondsSince(value) {
  return Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
}

function formatDuration(startedAt, completedAt, status) {
  if (!startedAt) {
    return "-";
  }

  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const seconds = Math.max(0, Math.round((end - new Date(startedAt).getTime()) / 1000));
  const suffix = status === "Running" && !completedAt ? " running" : "";

  if (seconds < 60) {
    return `${seconds}s${suffix}`;
  }

  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;

  if (minutes < 60) {
    return `${minutes}m ${remainingSeconds}s${suffix}`;
  }

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return `${hours}h ${remainingMinutes}m${suffix}`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
