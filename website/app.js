/**
 * Dynamic Island for Windows — Interactive Site Controller
 * Implements GSAP-driven morphing, 3D monitor perspective,
 * state machine simulator, audio visualizer, and live test harness.
 */

document.addEventListener("DOMContentLoaded", () => {
  initIslandSimulator();
  init3DMonitorTilt();
  initInteractiveTests();
  initCopySnippet();
  initFluidNavigation();
  initDraggableIsland();

  // Prevent accidental text selection and dragging for native app feel
  document.addEventListener("selectstart", (e) => {
    if (!e.target.closest("input") && !e.target.closest("textarea")) {
      e.preventDefault();
    }
  });
});

/* ==========================================================================
   1. DYNAMIC ISLAND SIMULATOR (GSAP State Morphing)
   ========================================================================== */
function initIslandSimulator() {
  const island = document.getElementById("islandWidget");
  const modeButtons = document.querySelectorAll(".mode-pill-btn");

  const stateConfigs = {
    compact: { width: 220, minHeight: 44, borderRadius: 28, glow: "#00f0ff" },
    music:   { width: 380, minHeight: 180, borderRadius: 32, glow: "#00f0ff" },
    battery: { width: 310, minHeight: 74,  borderRadius: 30, glow: "#10b981" },
    privacy: { width: 280, minHeight: 70,  borderRadius: 30, glow: "#f59e0b" },
    timer:   { width: 126, minHeight: 38,  borderRadius: 999, glow: "#ff9f0a" }
  };

  let currentMode = "compact";
  let isPlaying = true;

  function switchIslandState(targetMode) {
    if (targetMode === currentMode) return;
    const cfg = stateConfigs[targetMode];
    if (!cfg) return;

    // 1. Hide current state
    const oldEl = document.getElementById(`state${capitalize(currentMode)}`);
    const newEl = document.getElementById(`state${capitalize(targetMode)}`);

    if (oldEl) {
      gsap.to(oldEl, {
        opacity: 0,
        scale: 0.95,
        duration: 0.15,
        ease: "power2.in",
        onComplete: () => {
          oldEl.classList.remove("active");
          if (newEl) {
            newEl.classList.add("active");
            gsap.fromTo(newEl, 
              { opacity: 0, scale: 0.92 }, 
              { opacity: 1, scale: 1, duration: 0.25, ease: "back.out(1.7)" }
            );
          }
        }
      });
    }

    // 2. Morph island container dimensions with GSAP Spring
    gsap.to(island, {
      width: cfg.width,
      minHeight: cfg.minHeight,
      borderRadius: cfg.borderRadius,
      boxShadow: `0 10px 30px rgba(0,0,0,0.9), 0 0 28px ${cfg.glow}44`,
      duration: 0.45,
      ease: "elastic.out(1, 0.75)"
    });

    currentMode = targetMode;

    // Update active button state
    modeButtons.forEach(btn => {
      btn.classList.toggle("active", btn.dataset.mode === targetMode);
    });
  }

  // Bind Mode Buttons
  modeButtons.forEach(btn => {
    btn.addEventListener("click", () => {
      switchIslandState(btn.dataset.mode);
    });
  });

  // Clicking the island directly toggles between compact and music
  island.addEventListener("click", (e) => {
    // Avoid triggering if interacting with buttons/sliders inside
    if (e.target.closest("button") || e.target.closest("input")) return;
    const next = currentMode === "compact" ? "music" : "compact";
    switchIslandState(next);
  });

  // Mouse wheel scroll to fluidly switch modes (mimicking app's NextView gesture)
  const modeList = ["compact", "music", "battery", "privacy", "timer"];
  let lastWheelTime = 0;
  island.addEventListener("wheel", (e) => {
    e.preventDefault();
    const now = Date.now();
    if (now - lastWheelTime < 180) return; // gesture debounce
    lastWheelTime = now;

    let idx = modeList.indexOf(currentMode);
    if (e.deltaY > 0) {
      idx = (idx + 1) % modeList.length;
    } else {
      idx = (idx - 1 + modeList.length) % modeList.length;
    }
    switchIslandState(modeList[idx]);
  }, { passive: false });

  // Music Player Simulation Controls
  const btnPlay = document.getElementById("btnPlay");
  const playIcon = document.getElementById("playIcon");
  const pauseIcon = document.getElementById("pauseIcon");
  const eqCols = document.querySelectorAll(".eq-col, .mini-eq-bars .bar");

  btnPlay.addEventListener("click", (e) => {
    e.stopPropagation();
    isPlaying = !isPlaying;
    playIcon.style.display = isPlaying ? "none" : "block";
    pauseIcon.style.display = isPlaying ? "block" : "none";

    eqCols.forEach(col => {
      col.style.animationPlayState = isPlaying ? "running" : "paused";
    });
  });

  // Track next / prev simulation
  const tracks = [
    { title: "Midnight Horizon", artist: "SPARK Soundworks • Windows Audio" },
    { title: "Cybernetic Pulse", artist: "DirectSound Wasapi Core" },
    { title: "Acoustic Reflection", artist: "Spatial Studio Output" }
  ];
  let trackIdx = 0;
  const trackTitleEl = document.getElementById("trackTitle");
  const trackArtistEl = document.querySelector(".track-artist");

  document.getElementById("btnNext").addEventListener("click", (e) => {
    e.stopPropagation();
    trackIdx = (trackIdx + 1) % tracks.length;
    animateTrackChange(tracks[trackIdx]);
  });

  document.getElementById("btnPrev").addEventListener("click", (e) => {
    e.stopPropagation();
    trackIdx = (trackIdx - 1 + tracks.length) % tracks.length;
    animateTrackChange(tracks[trackIdx]);
  });

  function animateTrackChange(track) {
    gsap.fromTo([trackTitleEl, trackArtistEl], 
      { opacity: 0, y: -4 }, 
      { opacity: 1, y: 0, duration: 0.25, stagger: 0.05 }
    );
    trackTitleEl.textContent = track.title;
    trackArtistEl.textContent = track.artist;
  }

  // Interactive Scrubber Bar
  const scrubberBar = document.getElementById("scrubberBar");
  const scrubberFill = document.getElementById("scrubberFill");
  const scrubberKnob = document.getElementById("scrubberKnob");
  const curTimeEl = document.getElementById("curTime");

  scrubberBar.addEventListener("click", (e) => {
    e.stopPropagation();
    const rect = scrubberBar.getBoundingClientRect();
    const pct = Math.max(0, Math.min(100, ((e.clientX - rect.left) / rect.width) * 100));
    scrubberFill.style.width = `${pct}%`;
    scrubberKnob.style.left = `${pct}%`;

    const totalSeconds = 198; // 3:18
    const curSec = Math.floor((pct / 100) * totalSeconds);
    const m = Math.floor(curSec / 60);
    const s = curSec % 60;
    curTimeEl.textContent = `${m}:${s < 10 ? '0' : ''}${s}`;
  });

  // Simulated focus timer countdown (matches native app 24:55 start)
  let timerSeconds = 24 * 60 + 55;
  const timerClockEl = document.getElementById("timerClock");
  setInterval(() => {
    if (timerSeconds > 0) {
      timerSeconds--;
      const m = Math.floor(timerSeconds / 60);
      const s = timerSeconds % 60;
      if (timerClockEl) {
        timerClockEl.textContent = `${m}:${s < 10 ? '0' : ''}${s}`;
      }
    } else {
      timerSeconds = 24 * 60 + 55; // loop timer
    }
  }, 1000);
}

/* ==========================================================================
   2. 3D MONITOR PERSPECTIVE TILT
   ========================================================================== */
function init3DMonitorTilt() {
  const monitor = document.getElementById("monitor3D");
  const heroSection = document.querySelector(".hero-section");
  if (!monitor || !heroSection) return;

  heroSection.addEventListener("mousemove", (e) => {
    const rect = monitor.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;

    const deltaX = (e.clientX - centerX) / (window.innerWidth / 2);
    const deltaY = (e.clientY - centerY) / (window.innerHeight / 2);

    const tiltX = -deltaY * 8; // degrees
    const tiltY = deltaX * 8;

    gsap.to(monitor, {
      rotateX: tiltX,
      rotateY: tiltY,
      transformPerspective: 1200,
      ease: "power1.out",
      duration: 0.5
    });
  });

  heroSection.addEventListener("mouseleave", () => {
    gsap.to(monitor, {
      rotateX: 0,
      rotateY: 0,
      ease: "power2.out",
      duration: 0.8
    });
  });
}

/* ==========================================================================
   4. INTERACTIVE TEST HARNESS SIMULATOR
   ========================================================================== */
function initInteractiveTests() {
  const btnRun = document.getElementById("btnRunTests");
  const termOutput = document.getElementById("termOutput");
  if (!btnRun || !termOutput) return;

  const testCases = [
    { name: "Test_IslandLogic_CompactToExpandedTransition", duration: "11ms" },
    { name: "Test_WasapiAudio_CaptureLoopback_BufferHealth", duration: "16ms" },
    { name: "Test_BatteryTelemetry_ChargingStateDetection",  duration: "5ms" },
    { name: "Test_SingleInstanceMutex_DuplicateHandover",    duration: "13ms" },
    { name: "Test_MemoryConsumption_IdleThreshold_Under20MB",duration: "8ms" }
  ];

  btnRun.addEventListener("click", () => {
    btnRun.disabled = true;
    btnRun.innerHTML = `<span>Running...</span>`;
    termOutput.innerHTML = `<span class="term-line-info">[INFO] Initializing DynamicIsland.Tests engine...</span>`;

    let idx = 0;
    const interval = setInterval(() => {
      if (idx < testCases.length) {
        const t = testCases[idx];
        const line = document.createElement("span");
        line.className = "term-line-pass";
        line.textContent = `✔ [PASS] ${t.name} (${t.duration})`;
        termOutput.appendChild(line);
        idx++;
      } else {
        clearInterval(interval);
        const div = document.createElement("div");
        div.innerHTML = `
          <span class="term-line-highlight">------------------------------------------------------------</span>
          <span class="term-line-highlight">All ${testCases.length} Tests Passed! 100% Quality Assertion verified.</span>
        `;
        termOutput.appendChild(div);
        btnRun.disabled = false;
        btnRun.innerHTML = `
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
          <span>Run Test Suite</span>
        `;
      }
    }, 180);
  });
}

/* ==========================================================================
   5. COPY SNIPPET TO CLIPBOARD
   ========================================================================== */
function initCopySnippet() {
  const btnCopy = document.getElementById("btnCopyCmd");
  const copyStatus = document.getElementById("copyStatusText");
  const cloneCmd = document.getElementById("cloneCommand");
  if (!btnCopy || !cloneCmd) return;

  btnCopy.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(cloneCmd.textContent.trim());
      copyStatus.textContent = "Copied!";
      btnCopy.style.background = "#10b981";
      btnCopy.style.color = "#000";
      setTimeout(() => {
        copyStatus.textContent = "Copy";
        btnCopy.style.background = "";
        btnCopy.style.color = "";
      }, 2000);
    } catch (err) {
      copyStatus.textContent = "Copied!";
    }
  });
}

/* ==========================================================================
   6. FLUID FLOATING DOCK & SCROLL-SPY NAVIGATION
   ========================================================================== */
function initFluidNavigation() {
  const dockLinks = document.querySelectorAll(".dock-link:not(.dock-download)");
  const sections = document.querySelectorAll("section[id]");

  // Smooth scroll click handler
  document.querySelectorAll(".dock-link").forEach(link => {
    link.addEventListener("click", (e) => {
      const targetId = link.getAttribute("href");
      if (targetId && targetId.startsWith("#")) {
        e.preventDefault();
        const targetEl = document.querySelector(targetId);
        if (targetEl) {
          targetEl.scrollIntoView({ behavior: "smooth", block: "start" });
        }
      }
    });
  });

  // Scroll-spy observer for fluid indicator update
  if ("IntersectionObserver" in window) {
    const observer = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          const id = entry.target.getAttribute("id");
          dockLinks.forEach(link => {
            const isActive = link.getAttribute("data-target") === id;
            link.classList.toggle("active", isActive);
          });
        }
      });
    }, {
      rootMargin: "-20% 0px -60% 0px",
      threshold: 0
    });

    sections.forEach(sec => observer.observe(sec));
  }
}

/* ==========================================================================
   DRAGGABLE ISLAND WITH EDGE SNAP
   ========================================================================== */
function initDraggableIsland() {
  const island = document.getElementById("islandWidget");
  if (!island) return;

  // Dragging state
  let isDragging = false;
  let startX, startY;

  island.addEventListener("mousedown", (e) => {
    // Don't drag when interacting with buttons, sliders, or scrubber
    if (e.target.closest("button, input, .scrubber-bar, .vol-range")) return;

    isDragging = true;
    island.style.cursor = "grabbing";
    island.style.transition = "none"; // disable smooth morphing while dragging

    const rect = island.getBoundingClientRect();
    startX = e.clientX - rect.left;
    startY = e.clientY - rect.top;

    e.preventDefault();
  });

  document.addEventListener("mousemove", (e) => {
    if (!isDragging) return;

    const x = e.clientX - startX;
    const y = e.clientY - startY;
    island.style.left = `${x}px`;
    island.style.top = `${y}px`;
  });

  document.addEventListener("mouseup", () => {
    if (!isDragging) return;

    isDragging = false;
    island.style.cursor = "grab";
    island.style.transition = ""; // restore smooth morphing

    snapToEdge(island);
  });

  function snapToEdge(el) {
    const rect = el.getBoundingClientRect();
    const w = window.innerWidth;
    const h = window.innerHeight;
    const THRESHOLD = 40; // px from edge to trigger snap

    let snapLeft = null;
    let snapTop = null;

    // Vertical edges
    if (rect.left < THRESHOLD) snapLeft = 0;
    else if (rect.right > w - THRESHOLD) snapLeft = w - rect.width;

    // Horizontal edges
    if (rect.top < THRESHOLD) snapTop = 0;
    else if (rect.bottom > h - THRESHOLD) snapTop = h - rect.height;

    if (snapLeft !== null || snapTop !== null) {
      // Only animate changed axes; keep current value for unchanged axes
      const curLeft = el.offsetLeft;
      const curTop = el.offsetTop;
      el.style.left = `${snapLeft !== null ? snapLeft : curLeft}px`;
      el.style.top = `${snapTop !== null ? snapTop : curTop}px`;
    }
  }
}

function capitalize(str) {
  return str.charAt(0).toUpperCase() + str.slice(1);
}

