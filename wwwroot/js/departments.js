(() => {
  'use strict';
  const el = id => document.getElementById(id);
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
  const normalize = value => String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[đĐ]/g, 'd').toLocaleLowerCase('vi').trim();
  const number = value => Number(value || 0).toLocaleString('vi-VN');
  let departments = [];
  let loaded = false;
  const manager = d => d.manager && d.manager !== 'Chưa bổ nhiệm' ? escape(d.manager) : '<span class="department-unassigned">Chưa bổ nhiệm</span>';
  const status = d => `<span class="department-status ${d.status === 'active' ? 'is-active' : 'is-inactive'}">${d.status === 'active' ? 'Đang hoạt động' : 'Ngừng hoạt động'}</span>`;
  const link = d => `<a class="department-link" href="/Director/HumanResources?department=${encodeURIComponent(d.code)}" aria-label="Xem nhân sự ${escape(d.name)}">Xem nhân sự <i class="ti ti-chevron-right" aria-hidden="true"></i></a>`;

  function render() {
    if (!loaded) return;
    const terms = normalize(el('departmentSearch').value).split(/\s+/).filter(Boolean);
    const selectedStatus = el('departmentStatus').value;
    const list = departments.filter(d => (!selectedStatus || d.status === selectedStatus) && terms.every(term => normalize(`${d.name} ${d.code} ${d.manager}`).includes(term)));
    el('departmentCount').textContent = `Hiển thị ${list.length} / ${departments.length} phòng ban`;
    const empty = departments.length ? 'Không có phòng ban phù hợp. Thử đổi từ khóa hoặc trạng thái.' : 'Chưa có dữ liệu phòng ban.';
    el('departmentRows').innerHTML = list.length ? list.map(d => `<tr>
      <td><span class="department-name">${escape(d.name)}</span><span class="department-code">${escape(d.code)}</span></td>
      <td>${manager(d)}</td><td class="department-number">${number(d.headcount)}</td><td>${status(d)}</td><td>${link(d)}</td>
    </tr>`).join('') : `<tr><td colspan="5" class="department-empty">${empty}</td></tr>`;
    el('departmentCards').innerHTML = list.length ? list.map(d => `<article class="department-item">
      <div class="department-item-head"><div><h3 class="department-name">${escape(d.name)}</h3><span class="department-code">${escape(d.code)}</span></div>${status(d)}</div>
      <dl><dt>Trưởng phòng</dt><dd>${manager(d)}</dd><dt>Nhân sự hiện tại</dt><dd>${number(d.headcount)}</dd></dl>${link(d)}
    </article>`).join('') : `<p class="department-empty">${empty}</p>`;
  }

  async function load() {
    loaded = false;
    el('departmentError').hidden = true;
    el('departmentResults').hidden = false;
    el('departmentResults').setAttribute('aria-busy', 'true');
    el('departmentCount').textContent = 'Đang tải dữ liệu…';
    el('departmentRetry').disabled = true;
    try {
      const response = await fetch('/Director/Department/ApiList', { headers: { Accept:'application/json', 'X-Requested-With':'XMLHttpRequest' }, credentials:'same-origin' });
      if (!response.ok) throw new Error('Department request failed');
      const payload = await response.json();
      if (!payload.success || !Array.isArray(payload.data?.list) || !payload.data.summary) throw new Error('Invalid department response');
      departments = payload.data.list;
      const summary = payload.data.summary;
      el('departmentTotal').textContent = number(summary.departments);
      el('departmentActive').textContent = number(summary.activeDepartments);
      el('departmentEmployees').textContent = number(summary.employees);
      loaded = true;
      render();
    } catch {
      el('departmentError').hidden = false;
      el('departmentResults').hidden = true;
      el('departmentCount').textContent = 'Chưa tải được dữ liệu';
      for (const id of ['departmentTotal', 'departmentActive', 'departmentEmployees']) el(id).textContent = '—';
    } finally {
      el('departmentResults').setAttribute('aria-busy', 'false');
      el('departmentRetry').disabled = false;
    }
  }
  el('departmentFilters').addEventListener('submit', event => { event.preventDefault(); render(); });
  el('departmentSearch').addEventListener('input', render);
  el('departmentStatus').addEventListener('change', render);
  el('departmentRetry').addEventListener('click', load);
  load();
})();
