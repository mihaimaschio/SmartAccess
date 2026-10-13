// Fereastra de inrolare a amprentei, folosita si de pagina Membri, si de Admin.
// Se insereaza singura in pagina la prima folosire; se apeleaza cu
// openEnrollModal(userId, name, onDone).

const FINGERPRINT_ENROLL_SVG = `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12C2 6.5 6.5 2 12 2a10 10 0 0 1 8 4"/><path d="M5 19.5C5.5 18 6 15 6 12c0-3.3 2.7-6 6-6 2.8 0 5.2 1.9 5.8 4.6"/><path d="M10 19c.2-1.2.3-2.3.3-3.5 0-2 1.5-3.5 3.5-3.5.7 0 1.3.2 1.8.5"/><path d="M10.9 22c-.3-1.2-.5-2.4-.5-3.7 0-1 .8-1.8 1.8-1.8s1.8.8 1.8 1.8c0 .3 0 .6-.1.8"/><path d="M14 22c.4-1 .7-2 .7-3.1 0-.5.4-.9.9-.9"/></svg>`;

let _enrollPolling = null;
let _enrollUserId = null;
let _enrollSlot = null;
let _enrollOnDone = null;

function _ensureEnrollModal() {
  if (document.getElementById('modal-enroll-shared')) return;
  const wrap = document.createElement('div');
  wrap.innerHTML = `
    <div class="modal-backdrop" id="modal-enroll-shared">
      <div class="modal">
        <div class="modal-header">
          <h3>ENROLL FINGERPRINT</h3>
          <button class="modal-close" onclick="closeEnrollModal()">✕</button>
        </div>
        <div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:12px;">
          <span style="font-size:11px; font-weight:700; color:var(--accent); letter-spacing:1.5px;">ENROLLMENT IN PROGRESS</span>
          <div class="spinner" id="em-spinner"></div>
        </div>
        <div style="height:1px; background:var(--accent-dim); margin-bottom:14px;"></div>

        <div id="em-steps">
          <div class="enroll-step">
            <div class="step-icon done" id="em-step1-icon">✓</div>
            <div><div class="step-title" id="em-step1-title" style="color:var(--accent);">Enrollment started</div>
            <div class="step-subtitle" id="em-step1-sub">Command sent</div></div>
          </div>
          <div class="enroll-step">
            <div class="step-icon" id="em-step2-icon">2</div>
            <div><div class="step-title" id="em-step2-title">Place your finger</div>
            <div class="step-subtitle" id="em-step2-sub">Place finger on the sensor</div></div>
          </div>
          <div class="enroll-step">
            <div class="step-icon" id="em-step3-icon">3</div>
            <div><div class="step-title" id="em-step3-title">Finalizing</div>
            <div class="step-subtitle" id="em-step3-sub">Waiting...</div></div>
          </div>
        </div>

        <div id="em-animation" style="display:none; text-align:center; padding:20px; background:var(--bg-primary); border:1px solid var(--border); border-radius:var(--radius-lg); margin:14px 0;">
          <div id="em-icon" class="pulse" style="display:flex; align-items:center; justify-content:center; color:var(--warning);"></div>
          <div id="em-wait-text" style="font-size:13px; font-weight:600; color:var(--warning); margin-top:12px;">Waiting for fingerprint...</div>
        </div>

        <div id="em-result" style="display:none; padding:12px; border-radius:var(--radius-md); text-align:center; margin-top:14px;">
          <span id="em-result-icon" style="font-size:20px; font-weight:700;"></span>
          <span id="em-result-text" style="font-size:14px; font-weight:600; margin-left:8px;"></span>
        </div>

        <button class="btn btn-secondary" style="width:100%; justify-content:center; margin-top:14px;" id="em-btn-close" onclick="closeEnrollModal()">
          CLOSE
        </button>
      </div>
    </div>`;
  document.body.appendChild(wrap.firstElementChild);
}

function closeEnrollModal() {
  const el = document.getElementById('modal-enroll-shared');
  if (el) el.classList.remove('active');
  if (_enrollPolling) { clearInterval(_enrollPolling); _enrollPolling = null; }
}

function _emResetStep(prefix, num, title, sub) {
  document.getElementById(prefix + '-icon').className = 'step-icon';
  document.getElementById(prefix + '-icon').textContent = num;
  document.getElementById(prefix + '-title').textContent = title;
  document.getElementById(prefix + '-title').style.color = 'var(--text-muted)';
  document.getElementById(prefix + '-sub').textContent = sub;
}

function _emShowWaitingFinger() {
  document.getElementById('em-step2-icon').className = 'step-icon active';
  document.getElementById('em-step2-icon').textContent = '→';
  document.getElementById('em-step2-title').style.color = 'var(--warning)';
  document.getElementById('em-step2-title').textContent = 'Place your finger on the sensor';
  document.getElementById('em-animation').style.display = '';
  document.getElementById('em-icon').innerHTML = FINGERPRINT_ENROLL_SVG;
  document.getElementById('em-wait-text').textContent = 'Waiting for fingerprint...';
}

function _emStartPolling(name) {
  if (_enrollPolling) clearInterval(_enrollPolling);
  _enrollPolling = setInterval(async () => {
    try {
      const data = await api.get('/members/enroll-status');
      const status = (data.status || '').toLowerCase();

      if (status.includes('amprenta salvata') || (status.includes('salvat') && status.includes('succes'))) {
        await _emShowSuccess(name);
        return;
      }
      if (status.includes('eroare') || status.includes('error') || status.includes('timeout')) {
        _emShowError(data.status);
        return;
      }

      if (status.includes('ridica degetul')) {
        document.getElementById('em-step2-title').textContent = 'Lift your finger...';
        document.getElementById('em-wait-text').textContent = 'Lift your finger...';
      } else if (status.includes('acelasi deget')) {
        document.getElementById('em-step2-title').textContent = 'Place the same finger again';
        document.getElementById('em-wait-text').textContent = 'Place the same finger...';
      } else if (status.includes('pune degetul')) {
        document.getElementById('em-step2-title').textContent = 'Place your finger on the sensor';
      }
    } catch (e) {}
  }, 1000);
}

async function _emShowSuccess(name) {
  if (_enrollPolling) { clearInterval(_enrollPolling); _enrollPolling = null; }

  try {
    await api.post(`/members/${_enrollUserId}/enroll-confirm`, { slot: _enrollSlot });
  } catch (e) {
    _emShowError('Nu am putut salva amprenta in baza de date.');
    return;
  }

  document.getElementById('em-spinner').style.display = 'none';
  document.getElementById('em-animation').style.display = 'none';

  document.getElementById('em-step2-icon').className = 'step-icon done';
  document.getElementById('em-step2-icon').textContent = '✓';
  document.getElementById('em-step2-title').textContent = 'Fingerprint recorded';
  document.getElementById('em-step2-title').style.color = 'var(--accent)';

  document.getElementById('em-step3-icon').className = 'step-icon done';
  document.getElementById('em-step3-icon').textContent = '✓';
  document.getElementById('em-step3-title').textContent = 'Enrollment complete';
  document.getElementById('em-step3-title').style.color = 'var(--accent)';
  document.getElementById('em-step3-sub').textContent = name + ' has been added successfully';

  document.getElementById('em-result').style.display = '';
  document.getElementById('em-result').style.background = 'var(--success-bg)';
  document.getElementById('em-result-icon').textContent = '✓';
  document.getElementById('em-result-icon').style.color = 'var(--success)';
  document.getElementById('em-result-text').textContent = name + ' enrolled successfully';
  document.getElementById('em-result-text').style.color = 'var(--accent)';

  document.getElementById('em-btn-close').textContent = 'DONE';
  document.getElementById('em-btn-close').style.background = 'var(--accent)';
  document.getElementById('em-btn-close').style.color = 'var(--bg-primary)';

  if (_enrollOnDone) _enrollOnDone();
}

function _emShowError(msg) {
  if (_enrollPolling) { clearInterval(_enrollPolling); _enrollPolling = null; }
  document.getElementById('em-spinner').style.display = 'none';
  document.getElementById('em-animation').style.display = 'none';

  document.getElementById('em-step3-icon').className = 'step-icon error';
  document.getElementById('em-step3-icon').textContent = '✕';
  document.getElementById('em-step3-title').textContent = 'Enrollment failed';
  document.getElementById('em-step3-title').style.color = 'var(--danger)';
  document.getElementById('em-step3-sub').textContent = msg;

  document.getElementById('em-result').style.display = '';
  document.getElementById('em-result').style.background = 'var(--danger-bg)';
  document.getElementById('em-result-icon').textContent = '✕';
  document.getElementById('em-result-icon').style.color = 'var(--danger)';
  document.getElementById('em-result-text').textContent = 'Enrollment failed';
  document.getElementById('em-result-text').style.color = 'var(--danger)';
}

async function openEnrollModal(userId, name, onDone) {
  _ensureEnrollModal();
  _enrollOnDone = onDone || null;
  _enrollUserId = userId;
  _enrollSlot = null;

  document.getElementById('em-result').style.display = 'none';
  document.getElementById('em-animation').style.display = 'none';
  document.getElementById('em-spinner').style.display = '';
  document.getElementById('em-step1-sub').textContent = 'Command sent for ' + name;

  _emResetStep('em-step2', '2', 'Place your finger', 'Place finger on the sensor');
  _emResetStep('em-step3', '3', 'Finalizing', 'Waiting...');

  document.getElementById('em-btn-close').textContent = 'CLOSE';
  document.getElementById('em-btn-close').style.background = 'var(--bg-card)';
  document.getElementById('em-btn-close').style.color = 'var(--text-muted)';

  document.getElementById('modal-enroll-shared').classList.add('active');

  try {
    const enrollRes = await api.post(`/members/${userId}/enroll`);
    _enrollSlot = enrollRes.slot;
    setTimeout(_emShowWaitingFinger, 1500);
    _emStartPolling(name);
  } catch (e) {
    _emShowError(e.message);
  }
}
