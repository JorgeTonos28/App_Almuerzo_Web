const { createApp, ref, computed, onMounted, reactive, nextTick, watch } = Vue;

createApp({
  render: AlmuerzoRender,
  setup() {
    const csrfToken = document.querySelector("meta[name=csrf-token]").content;
    const appearance = window.__INITIAL_DATA__?.branding || {};
    const footerText = ref(appearance.footerText || '');
    const footerSignatureUrl = ref(appearance.footerSignatureUrl || '');
    const faviconUrl = ref(appearance.faviconUrl || '/assets/favicon.svg');
    const brandingForm = reactive({ footerText: footerText.value, favicon: null, stamp: null, resetFavicon: false, removeStamp: false });
    const brandingSaving = ref(false);
    const brandingMessage = ref('');
    const configPanel = reactive({ section: 'operation', saving: '', message: '', error: '', testing: false });
    const configDraft = reactive({});
    const configSections = [
      { id: 'operation', label: 'Operación', icon: 'fa-clock' },
      { id: 'mail', label: 'Correos', icon: 'fa-envelope' },
      { id: 'provider', label: 'Proveedor y precio', icon: 'fa-utensils' },
      { id: 'appearance', label: 'Apariencia', icon: 'fa-palette' },
      { id: 'announcements', label: 'Avisos', icon: 'fa-bullhorn' }
    ];
    const configKeysBySection = {
      operation: ['APP_TITLE', 'APP_URL', 'HORA_ENVIO', 'HORA_RECORDATORIO', 'MINUTOS_PREV_CIERRE'],
      mail: ['MAIL_SENDER_NAME', 'ADMIN_EMAILS', 'TEST_EMAIL_MODE', 'TEST_EMAIL_DEST', 'RESPONSIBLES_EMAILS_JSON'],
      provider: ['MEAL_PRICE_CURRENT']
    };
    const configCloseTime = computed(() => {
      const parts = String(configDraft.HORA_ENVIO || '15:00').split(':').map(Number);
      const total = parts[0] * 60 + parts[1] - Number(configDraft.MINUTOS_PREV_CIERRE || 0);
      return Number.isFinite(total) && total >= 0 ? `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}` : '—';
    });
    const clearBrandingFile = kind => { brandingForm[kind] = null; const input = document.getElementById('branding-' + kind); if (input) input.value = ''; };
    const selectBrandingFile = (event, kind) => {
      brandingForm[kind] = event.target.files?.[0] || null;
      if (kind === 'favicon') brandingForm.resetFavicon = false; else brandingForm.removeStamp = false;
      brandingMessage.value = '';
    };
    const saveBranding = async () => {
      if (brandingSaving.value) return;
      brandingSaving.value = true; brandingMessage.value = '';
      try {
        const body = new FormData(); body.append('footerText', brandingForm.footerText);
        body.append('resetFavicon', String(brandingForm.resetFavicon)); body.append('removeStamp', String(brandingForm.removeStamp));
        for (const kind of ['favicon', 'stamp']) if (brandingForm[kind]) body.append(kind, brandingForm[kind]);
        const response = await fetch('/branding', { method: 'POST', headers: { 'X-CSRF-TOKEN': csrfToken }, body });
        if (response.status === 401 || response.status === 403) throw new Error('Tu sesión no tiene permiso para guardar estos cambios. Inicia sesión como administrador general.');
        if (response.status === 413) throw new Error('Las imágenes exceden el tamaño permitido.');
        const result = await response.json();
        if (!response.ok || !result.ok) throw new Error(result.msg || 'No se pudo guardar. Verifica el tamaño de las imágenes y tu sesión.');
        footerText.value = result.branding.footerText; footerSignatureUrl.value = result.branding.footerSignatureUrl; faviconUrl.value = result.branding.faviconUrl;
        brandingForm.footerText = footerText.value; brandingForm.resetFavicon = false; brandingForm.removeStamp = false;
        clearBrandingFile('favicon'); clearBrandingFile('stamp');
        document.getElementById('app-favicon').href = faviconUrl.value;
        brandingMessage.value = 'Cambios guardados.';
      } catch (error) { brandingMessage.value = error.message || 'No se pudo guardar la configuración.'; }
      finally { brandingSaving.value = false; }
    };
    // === ESTADO (STATE) ===
    const loading = ref(true);
    const submitting = ref(false);
    const activeUsersCount = ref(0);

    // Datos del usuario y app
    const user = ref({});
    const activeUser = ref({});
    const organizationManaged = ref(false);
    const regionalBlockSaving = ref('');
    const userStatusSaving = ref('');
    const deptUsers = ref([]);
    const impersonateEmail = ref('');
    const menu = ref({});
    const myOrder = ref(null);
    const adminData = ref(null);

    // Manejo de Fechas (Nuevo)
    const currentDate = ref(''); // Fecha seleccionada actualmente (YYYY-MM-DD)
    const dates = ref([]);       // Lista de fechas disponibles (tabs)
    const allMenus = ref({});
    const allOrders = ref({});
    const menuCategories = ref([]);
    const remindersEnabled = ref(true);
    const mealPricing = ref({ current: 57, history: [{ from: '1900-01-01', price: 57 }] });
    const currentUserPrefs = ref({});
    const todayYmd = ref('');
    const calendarYear = ref(new Date().getFullYear());
    const calendarHolidays = computed(() => adminState.holidays.filter(h => h.date.startsWith(String(calendarYear.value))).sort((a,b) => a.date.localeCompare(b.date)));
    const serverHour = ref(12);
    const serverTime = ref('');

    // Aviso General
    const announcementConfig = ref({ enabled: false, id: '', expiresOn: '', maxDismiss: 3, slides: [] });
    const showAnnouncementModal = ref(false);
    const announcementSlideIndex = ref(0);
    const announcementEditModal = ref({ open: false, enabled: true, expiresOn: '', maxDismiss: 3, slides: [], saving: false });

    // Proveedor y Valoraciones
    const providerInfo = ref({ name: 'Proveedor de Alimentos', periodId: 'PROV_2026_01', periodStart: '' });
    const userMealRatings = ref({});
    const userProviderRating = ref(null);
    const mealRatingModal = ref({ open: false, date: '', dateLabel: '', orderId: '', rating: 5, comment: '', dishes: '', saving: false, error: null });
    const providerRatingModal = ref({ open: false, rating: 5, comment: '', saving: false, error: null });
    const providerResetModal = ref({ open: false, providerName: '', saving: false, error: null });
    const ratingsSummary = ref({ providerStats: null, mealStats: null, loading: false });
    const satisfactionCommunityModal = ref({ open: false, activeTab: 'PROVIDER', loading: false, data: null });
    const chefGameState = ref({
      score: 0,
      remainingSeconds: 900,
      dailyLimitSeconds: 900,
      streak: 0,
      bestStreak: 0,
      penaltySeconds: 0,
      monthLabel: ''
    });
    const chefGame = reactive({ active: false, dirty: false, syncing: false });
    const lunchRush = createLunchRush(state => { setChefGameState(state); chefRanking.loaded = false; });
    const rush = lunchRush.state;
    const startRush = lunchRush.start, finishRush = lunchRush.finish, moveRush = lunchRush.move, rushClock = lunchRush.clock;
    const chefRanking = reactive({
      loading: false,
      loaded: false,
      error: '',
      rows: [],
      currentUser: null,
      monthKey: '',
      monthLabel: '',
      showRules: false, scope: 'NATIONAL', page: 1, total: 0, pageSize: 50
    });

    // Summary Date Range
    const summaryDateStart = ref('');
    const summaryDateEnd = ref('');

    // Formulario
    const comments = ref('');
    const selection = reactive(new Set());
    const selectionData = reactive({});

    const deptAdminSearch = ref('');

    // Logo Animation State
    const logoAnimating = ref(false);
    const bubbleVisible = ref(false);
    const bubbleText = ref('');
    const bubbleTimer = ref(null);
    const heartbeatTimer = ref(null);

    // Message Modal State
    const msgModal = reactive({
       open: false,
       title: '',
       message: '',
       type: 'ALERT', // ALERT | CONFIRM
       onConfirm: null
    });

    const showModal = (msg, title = 'Atención', type = 'ALERT', onOk = null) => {
       msgModal.message = msg;
       msgModal.title = title;
       msgModal.type = type;
       msgModal.onConfirm = onOk;
       msgModal.open = true;
    };

    const startHeartbeat = () => {
       if (!activeUser.value?.email) return;
       if (heartbeatTimer.value) return;
       runHeartbeat();
       heartbeatTimer.value = window.setInterval(runHeartbeat, 45000);
    };

    const stopHeartbeat = () => {
       if (!heartbeatTimer.value) return;
       window.clearInterval(heartbeatTimer.value);
       heartbeatTimer.value = null;
    };

    const runHeartbeat = () => {
       almuerzo.run
         .withSuccessHandler(res => {
            if (res && res.count !== null) {
               activeUsersCount.value = res.count;
            }
         })
         .withFailureHandler(() => { /* Silencioso en error */ })
         .apiHeartbeat();
    };

    const closeMsgModal = () => {
       msgModal.open = false;
       msgModal.onConfirm = null;
    };

    const confirmMsgModal = () => {
       if(msgModal.onConfirm) msgModal.onConfirm();
       closeMsgModal();
    };

    // ADMIN STATE
    const viewMode = ref('USER'); // USER | ADMIN
    const adminState = reactive({
       loading: false,
       tab: 'USUARIOS',
       users: [],
       orders: [],
       departments: [],
       deptMap: {}, // ID -> Name
       config: {},
       configList: [],
       configKeys: [],
       holidays: [],
       menuCategories: [],
       userModal: { open: false, isNew: true, data: {} },
       deptModal: { open: false, isNew: true, saving: false, data: {} },
       configEdit: false,
       filters: {
          orders: { search: '', dateStart: '', dateEnd: '', status: '', open: false },
          users: { search: '' }
       },
       // Menu Editor
       menuDate: '',
       menuItems: [],
       menuModal: { open: false, isNew: true, data: {} },
       categoryManagerModal: { open: false, view: 'LIST' },
       categoryModal: { open: false, isNew: true, data: { combinable_con: [] } },
       // Menu Import
       menuImportModal: { open: false, step: 1, weekStart: '', text: '', preview: [], error: null },
       // Holiday Editor
       holidayModal: { open: false, data: {} },
       nextBusinessDay: '',
       responsiblesList: []
    });

    // === COMPUTED PROPERTIES ===
    const isAdmin = computed(() => activeUser.value.rol && ['ADMIN_GEN', 'ADMIN_REG', 'ADMIN_DEP'].includes(activeUser.value.rol));
    const isDeptAdmin = computed(() => activeUser.value.rol === 'ADMIN_DEP');
    const canManageRegion = computed(() => ['ADMIN_GEN', 'ADMIN_REG'].includes(activeUser.value.rol));
    const regions = ref(window.__INITIAL_DATA__?.regions || []);
    const regionalId = ref(window.__INITIAL_DATA__?.regionalId || 'central');
    const regionalName = ref(window.__INITIAL_DATA__?.regionalName || '');
    const regionalForm = reactive({ id: '', nombre: '', alias: '', directoryCode: '', directoryKey: '', directoryManaged: false, estado: 'ACTIVO', ...(regions.value.find(r => r.id === regionalId.value) || {}) });
    const regionalSaving = ref(false);
    const regionalError = ref('');
    const saveRegion = () => {
      if (regionalSaving.value) return; regionalSaving.value = true; regionalError.value = '';
      almuerzo.run.withSuccessHandler(res => { regionalSaving.value = false; if (res.ok) { regionalForm.id = res.id; const region = { ...regionalForm, displayName: regionalForm.alias.trim() || regionalForm.nombre }; const index = regions.value.findIndex(r => r.id === res.id); if (index < 0) regions.value.push(region); else regions.value.splice(index, 1, region); if (regionalId.value === res.id) regionalName.value = region.displayName; } else regionalError.value = res.msg; }).withFailureHandler(err => { regionalSaving.value = false; regionalError.value = err.message; }).apiSaveRegion({ ...regionalForm });
    };
    const editRegion = region => Object.assign(regionalForm, { ...region });
    const isAdminGen = computed(() => activeUser.value.rol === 'ADMIN_GEN');
    const adminTabs = computed(() => isAdminGen.value ? ['USUARIOS','PEDIDOS','VALORACIONES','DEPTOS','CONFIG','MENU','DIAS_LIBRES'] : (canManageRegion.value ? ['USUARIOS','PEDIDOS','VALORACIONES','DEPTOS','AVISOS','MENU','DIAS_LIBRES'] : ['USUARIOS','PEDIDOS','VALORACIONES']));
    const canBlockUser = u => canManageRegion.value && ['USER','ADMIN_DEP'].includes(u.rol) && u.email !== activeUser.value.email;
    const isTestEmailMode = computed(() => String(adminState.config['TEST_EMAIL_MODE'] || '').trim().toUpperCase() === 'TRUE');
    const chefScore = computed(() => Number(chefGameState.value?.score || 0));
    const chefRemainingSeconds = computed(() => Math.max(0, Number(chefGameState.value?.remainingSeconds || 0)));
    const chefGameAvailable = computed(() => chefRemainingSeconds.value > 0);
    const chefDailyProgress = computed(() => {
      const limit = Number(chefGameState.value?.dailyLimitSeconds || 900);
      const used = Math.max(0, Number(chefGameState.value?.usedSeconds || 0));
      if (!limit) return 0;
      return Math.min(100, Math.round((used / limit) * 100));
    });
    const chefRemainingLabel = computed(() => {
      const total = chefRemainingSeconds.value;
      const minutes = Math.floor(total / 60);
      const seconds = total % 60;
      return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
    });

    const menuLoaded = computed(() => menu.value && Object.keys(menu.value).length > 0);
    const chefGameCardVisible = computed(() => {
      if (viewMode.value !== 'USER' || loading.value) return false;
      if (myOrder.value) return true;
      return !menuLoaded.value;
    });
    const chefGameCardKey = computed(() => {
      if (myOrder.value) {
        return `${currentDate.value || 'pedido'}:${myOrder.value.id || 'pedido'}`;
      }
      return `${currentDate.value || 'sin-fecha'}:sin-menu`;
    });
    const chefGameInCooldown = computed(() => false);

    // FILTERED LISTS
    const filteredUsers = computed(() => {
       const q = adminState.filters.users.search.toLowerCase();
       if (!q) return adminState.users;
       return adminState.users.filter(u =>
          u.nombre.toLowerCase().includes(q) ||
          u.email.toLowerCase().includes(q) ||
          (u.codigo && String(u.codigo).includes(q)) ||
          (u.departamento || '').toLowerCase().includes(q) || (u.adDepartment || '').toLowerCase().includes(q)
       );
    });

    const filteredAdminUsers = computed(() => {
       const q = deptAdminSearch.value.toLowerCase();
       if (!q) return [];
        return adminState.users.filter(u => u.departamentoId === adminState.deptModal.data.id && u.estado === 'ACTIVO' && !u.regionalBlocked && ['USER','ADMIN_DEP','ADMIN_REG','ADMIN_GEN'].includes(u.rol) && (u.nombre.toLowerCase().includes(q) || u.email.toLowerCase().includes(q)));
     });

    const filteredOrders = computed(() => {
       const f = adminState.filters.orders;
       const q = f.search.toLowerCase();
       return adminState.orders.filter(o => {
          // Text Search (all fields including Creado Por and Dept)
          if (q && !(
             o.nombre.toLowerCase().includes(q) ||
             o.email.toLowerCase().includes(q) ||
             o.resumen.toLowerCase().includes(q) ||
             (o.creado_por && o.creado_por.toLowerCase().includes(q)) ||
             (o.dept && o.dept.toLowerCase().includes(q))
          )) return false;

          // Date Range Match
          if (f.dateStart && o.date < f.dateStart) return false;
          if (f.dateEnd && o.date > f.dateEnd) return false;

          // Status Match
          if (f.status && o.estado !== f.status) return false;

          return true;
       });
    });

    // Categorías configuradas por el administrador, ordenadas por su prioridad.
    const categoryById = computed(() => {
      const map = {};
      menuCategories.value.forEach(category => { map[category.id] = category; });
      return map;
    });

    const sortCategoryIds = (ids) => {
      return [...ids].sort((a, b) => {
        const categoryA = categoryById.value[a];
        const categoryB = categoryById.value[b];
        const orderA = categoryA ? Number(categoryA.orden || 999) : 999;
        const orderB = categoryB ? Number(categoryB.orden || 999) : 999;
        const nameA = categoryA ? categoryA.nombre : a;
        const nameB = categoryB ? categoryB.nombre : b;
        return orderA - orderB || nameA.localeCompare(nameB, 'es');
      });
    };

    const activeMenuCategories = computed(() => menuCategories.value.filter(category => category.estado === 'ACTIVO'));
    const adminMenuCategories = computed(() => {
      const ids = new Set(menuCategories.value.map(category => category.id));
      adminState.menuItems.forEach(item => ids.add(item.cat));
      return sortCategoryIds(ids).map(id => categoryById.value[id] || { id, nombre: String(id).replace(/_/g, ' '), orden: 999, estado: 'INACTIVO' });
    });
    const categories = computed(() => {
      if (!menu.value) return [];
      const cats = Object.keys(menu.value);
      return sortCategoryIds(cats);
    });

    const selectedCount = computed(() => selection.size);
    const minorMenuWords = new Set(['de', 'del', 'la', 'las', 'el', 'los', 'y', 'e', 'o', 'u', 'con', 'al', 'en']);
    const multiSelectCats = ['Vegetariana', 'Caldo', 'Opcion_Rapida'];

    const formatMenuText = (value) => {
      if (value === null || value === undefined) return '';

      const cleaned = String(value).trim().replace(/\s+/g, ' ');
      if (!cleaned) return '';

      return cleaned
        .toLowerCase()
        .split(' ')
        .map((word, index) => {
          return word
            .split(/([/-])/)
            .map(part => {
              if (!part || part === '/' || part === '-') return part;
              if (index > 0 && minorMenuWords.has(part)) return part;
              return part.charAt(0).toUpperCase() + part.slice(1);
            })
            .join('');
        })
        .join(' ');
    };

    const formatCurrency = (value) => {
      const amount = Number(value || 0);
      return new Intl.NumberFormat('es-DO', {
        style: 'currency',
        currency: 'DOP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
      }).format(amount);
    };

    const getMealPriceForDate = (dateStr) => {
      const fallback = Number(mealPricing.value?.current || 57);
      const history = Array.isArray(mealPricing.value?.history) ? mealPricing.value.history : [];
      const sortedHistory = history
        .filter(entry => entry && entry.from && entry.price !== undefined)
        .slice()
        .sort((a, b) => a.from.localeCompare(b.from));

      let resolvedPrice = fallback;
      sortedHistory.forEach(entry => {
        if (entry.from <= dateStr) {
          resolvedPrice = Number(entry.price || resolvedPrice);
        }
      });

      return resolvedPrice;
    };

    const resetSelectionState = () => {
      selection.clear();
      for (const prop of Object.getOwnPropertyNames(selectionData)) {
        delete selectionData[prop];
      }
      comments.value = '';
    };

    const shouldScrollSummaryCards = computed(() => userWeeklySummary.value.length > 8);

    // === SUMMARY TABLE HELPERS ===
    const summaryHeaders = computed(() => {
      const ids = new Set(menuCategories.value.map(category => category.id));
      Object.values(allOrders.value).forEach(order => {
        const categories = order && order.detalle && Array.isArray(order.detalle.categorias) ? order.detalle.categorias : [];
        categories.forEach(category => ids.add(category));
      });
      return sortCategoryIds(ids);
    });

    // Initial Default Range check
    const isDefaultSummary = computed(() => {
       if (!summaryDateStart.value || !summaryDateEnd.value) return true;
       const today = new Date();
       const toYMD = (d) => d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0');
       const start = toYMD(today);
       const next = new Date(today); next.setDate(today.getDate() + 7);
       const end = toYMD(next);
       return summaryDateStart.value === start && summaryDateEnd.value === end;
    });

    const summaryTitle = computed(() => isDefaultSummary.value ? "Mi Resumen Semanal" : "Resumen Diario");

    const userWeeklySummary = computed(() => {
      if (!summaryDateStart.value || !summaryDateEnd.value) return [];

      const start = new Date(summaryDateStart.value + 'T12:00:00');
      const end = new Date(summaryDateEnd.value + 'T12:00:00');
      const list = [];

      // Generate dates in range
      for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
          const ymd = d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0');
          // Skip weekends? Keeping them hidden if no order logic or just skip them entirely
          // Requirement doesn't strictly say hide weekends but usually corporate lunch is Mon-Fri.
          // However if a holiday is set on a weekday, it should probably appear if filtered?
          // Let's stick to: If it's Saturday(6) or Sunday(0), skip.
          if (d.getDay() === 0 || d.getDay() === 6) continue;

          const label = d.toLocaleDateString('es-DO', {weekday:'long', day:'numeric', month:'numeric'});
          // Capitalize first letter
          const finalLabel = label.charAt(0).toUpperCase() + label.slice(1);

          list.push({ value: ymd, label: finalLabel });
      }

      return list.map(d => {
        const order = allOrders.value[d.value];
        const row = { date: d.value, label: d.label, items: {}, mealPrice: getMealPriceForDate(d.value) };
        let hasItems = false;

        if (order && order.detalle) {
          const det = order.detalle;
          if (Array.isArray(det.categorias) && Array.isArray(det.items)) {
             det.categorias.forEach((cat, idx) => {
                if (!row.items[cat]) {
                   row.items[cat] = det.items[idx];
                } else {
                   row.items[cat] += ', ' + det.items[idx];
                }
                hasItems = true;
             });
          }
        }
        return hasItems ? row : null;
      }).filter(r => r !== null);
    });

    const userSummaryTotalCost = computed(() => {
      return userWeeklySummary.value.reduce((total, row) => total + Number(row.mealPrice || 0), 0);
    });

    const displayDate = computed(() => {
      const d = dates.value.find(x => x.value === currentDate.value);
      return d ? d.label : currentDate.value;
    });

    const isCutoffPassed = computed(() => {
      if (!currentDate.value) return true;
      if (dates.value.length === 0) return true;
      return !dates.value.some(d => d.value === currentDate.value);
    });

    // === REGLAS DE NEGOCIO (VALIDACIONES) ===
    const categoryKey = id => String(id || '').split(':').pop();
    const specialCats = ['Vegetariana', 'Caldo', 'Opcion_Rapida'];
    // exclusionCats: Categorías que no pueden mezclarse entre sí
    // Nota: En el requerimiento original Arroces no va con Víveres.

    // Helper: Array de categorías seleccionadas actualmente
    const selectedCats = computed(() => {
      const cats = new Set();
      selection.forEach(id => {
        if(selectionData[id]) cats.add(selectionData[id].cat);
      });
      return Array.from(cats);
    });

    const isSpecialSelected = computed(() => {
      return selectedCats.value.some(c => {
        const cat = categoryById.value[c];
        return cat ? !cat.es_combinable : specialCats.includes(categoryKey(c));
      });
    });

    // Validar si una categoría entera debe bloquearse (gris)
    const isCatDisabled = (cat) => {
      const targetCat = categoryById.value[cat];
      const selected = selectedCats.value;
      if (selected.length === 0) return false;

      // 1. Si la categoría ya está seleccionada, no se deshabilita
      if (selected.includes(cat)) return false;

      // 2. Verificar reglas dinámicas de las categorías ya seleccionadas
      for (const selCatId of selected) {
        const selCat = categoryById.value[selCatId];
        if (selCat) {
          if (!selCat.es_combinable) {
            const allowed = Array.isArray(selCat.combinable_con) ? selCat.combinable_con : [];
            if (!allowed.includes(cat)) return true;
          } else if (Array.isArray(selCat.combinable_con) && selCat.combinable_con.length > 0) {
            if (!selCat.combinable_con.includes(cat)) return true;
          }
        }
      }

      // 3. Verificar si la categoría objetivo permite combinarse con las seleccionadas
      if (targetCat) {
        if (!targetCat.es_combinable) {
          const allowed = Array.isArray(targetCat.combinable_con) ? targetCat.combinable_con : [];
          if (selected.some(sId => !allowed.includes(sId))) return true;
        } else if (Array.isArray(targetCat.combinable_con) && targetCat.combinable_con.length > 0) {
          if (selected.some(sId => !targetCat.combinable_con.includes(sId))) return true;
        }
      }

      // 4. Regla Granos (Estricta)
      if (selected.some(c => categoryKey(c) === 'Granos')) {
        const allowed = ['Granos', 'Arroces', 'Carnes', 'Ensaladas', 'Caldo', 'Frituritas'];
        if (!allowed.includes(categoryKey(cat))) return true;
      }

      if (categoryKey(cat) === 'Granos') {
         if (selected.some(c => ['Viveres', 'Vegetariana', 'Opcion_Rapida'].includes(categoryKey(c)))) return true;
         let hasNonWhiteRice = false;
         selection.forEach(id => {
            const item = selectionData[id];
            if (item && categoryKey(item.cat) === 'Arroces' && !item.plato.toLowerCase().includes('arroz blanco')) hasNonWhiteRice = true;
         });
         if (hasNonWhiteRice) return true;
      }

      // 5. Regla Arroz vs Viveres (Mutuamente exclusivos)
      if (categoryKey(cat) === 'Arroces' && selected.some(c => categoryKey(c) === 'Viveres')) return true;
      if (categoryKey(cat) === 'Viveres' && selected.some(c => categoryKey(c) === 'Arroces')) return true;

      return false;
    };

    // Validar items específicos
    const isItemDisabled = (cat, item) => {
       if (isCatDisabled(cat)) return true;
       // Si Granos está seleccionado, y estamos en Arroces, solo Arroz Blanco está permitido.
       if (categoryKey(cat) === 'Arroces' && selectedCats.value.some(c => categoryKey(c) === 'Granos')) {
          if (!item.plato.toLowerCase().includes('arroz blanco')) return true;
       }
       return false;
    };

    // Warnings específicos
    const showGrainWarning = computed(() => {
      // Si seleccionó Granos
      if (!selectedCats.value.some(c => categoryKey(c) === 'Granos')) return false;

      // Verificar si hay algún plato que contenga "Arroz Blanco" (case insensitive)
      let hasWhiteRice = false;
      selection.forEach(id => {
        const item = selectionData[id];
        if (item && categoryKey(item.cat) === 'Arroces' && item.plato.toLowerCase().includes('arroz blanco')) {
          hasWhiteRice = true;
        }
      });
      return !hasWhiteRice; // Muestra warning si NO hay arroz blanco
    });

    const validationError = computed(() => {
      if (selectedCount.value === 0) return null;
      if (showGrainWarning.value) return "Debes seleccionar Arroz Blanco para acompañar los granos.";
      return null;
    });

    const hasBlockingErrors = computed(() => {
      return selectedCount.value === 0 || validationError.value !== null;
    });

    // === ACCIONES (ACTIONS) ===

    const applyInitData = (res) => {
      if (res.empty) {
        dates.value = [];
        menu.value = {};
      }

      user.value = res.user;
      activeUser.value = res.activeUser || {};
      organizationManaged.value = !!res.organizationManaged;
      deptUsers.value = res.deptUsers || [];
      menu.value = res.menu || {};
      myOrder.value = res.myOrder || null;
      allMenus.value = res.allMenus || {};
      allOrders.value = res.allOrders || {};
      menuCategories.value = res.menuCategories || menuCategories.value;
      if (res.menuCategories) adminState.menuCategories = res.menuCategories;
      adminData.value = res.adminData || null;
      currentUserPrefs.value = res.userPrefs || {};
      remindersEnabled.value = res.userPrefs?.reminders !== false;

      if (res.deptMap) adminState.deptMap = res.deptMap;
      if (res.nextBusinessDay) adminState.nextBusinessDay = res.nextBusinessDay;

      mealPricing.value = res.mealPricing || { current: 57, history: [{ from: '1900-01-01', price: 57 }] };
      todayYmd.value = res.todayYmd || todayYmd.value;
      serverHour.value = res.serverHour !== undefined ? res.serverHour : new Date().getHours();
      serverTime.value = res.serverTime || new Date().toISOString();

      // Aviso General
      announcementConfig.value = res.announcementConfig || { enabled: false, id: '', expiresOn: '', maxDismiss: 3, slides: [] };
      if (announcementConfig.value.enabled) {
        const today = todayYmd.value || new Date().toISOString().substring(0, 10);
        const expires = announcementConfig.value.expiresOn;
        const isNotExpired = !expires || today <= expires;
        const dismissCount = Number(res.userPrefs?.announcements?.[announcementConfig.value.id] || 0);
        const maxDismiss = Number(announcementConfig.value.maxDismiss || 3);
        if (isNotExpired && dismissCount < maxDismiss) {
          showAnnouncementModal.value = true;
          announcementSlideIndex.value = 0;
        }
      }

      // Proveedor y Valoraciones
      providerInfo.value = res.providerInfo || { name: 'Proveedor de Alimentos', periodId: 'PROV_2026_01', periodStart: '' };
      userMealRatings.value = res.userMealRatings || {};
      userProviderRating.value = res.userProviderRating || null;

      setChefGameState(res.chefGame || {});
      if (res.activeUser?.email) startHeartbeat();
      else stopHeartbeat();

      currentDate.value = res.currentDate;
      dates.value = res.dates || [];

      if (['ADMIN_GEN', 'ADMIN_REG', 'ADMIN_DEP'].includes(res.activeUser?.rol) && adminState.users.length === 0 && !adminState.loading) {
        window.setTimeout(() => loadAdminData(false, { silent: true }), 120);
      }
    };

    const applyDateViewData = (res) => {
      currentDate.value = res.currentDate;
      menu.value = res.menu || {};
      myOrder.value = res.myOrder || null;
      allMenus.value = { ...allMenus.value, [res.currentDate]: res.menu || {} };

      const nextOrders = { ...allOrders.value };
      if (res.myOrder) nextOrders[res.currentDate] = res.myOrder;
      else delete nextOrders[res.currentDate];
      allOrders.value = nextOrders;

      if (res.adminData) adminData.value = res.adminData;
      resetSelectionState();
    };

    const setChefGameState = (state = {}) => {
      chefGameState.value = {
        score: 0,
        hits: 0,
        misses: 0,
        usedSeconds: 0,
        remainingSeconds: 900,
        dailyLimitSeconds: 900,
        streak: 0,
        bestStreak: 0,
        penaltySeconds: 0,
        monthLabel: '',
        ...state
      };
      chefGame.dirty = false;
    };

    const stopChefGame = () => { if (rush.active) return finishRush(); };
    const scheduleChefGameStart = () => {};
    const syncChefGame = async (silent, done) => { if (rush.id) await finishRush(); if (done) done(!rush.pending); };
    const openChefRanking = async () => {
      if (rush.active || rush.pending) await finishRush();
      viewMode.value = 'RANKING'; loadChefRanking(true);
    };
    const returnToOrders = () => {
      viewMode.value = 'USER';
      nextTick(() => scheduleChefGameStart(false));
    };

    const loadChefRanking = (force = false) => {
      if (chefRanking.loading) return;
      if (chefRanking.loaded && !force) return;

      chefRanking.loading = true;
      chefRanking.error = '';
      almuerzo.run
        .withSuccessHandler(res => {
          chefRanking.loading = false;
          if (res && res.ok) {
            chefRanking.loaded = true;
            chefRanking.rows = res.rows || [];
            chefRanking.currentUser = res.currentUser || null;
            chefRanking.monthKey = res.monthKey || '';
            chefRanking.monthLabel = res.monthLabel || '';
             chefRanking.page = res.page || 1; chefRanking.total = res.total || 0; chefRanking.pageSize = res.pageSize || 50;
          } else {
            chefRanking.error = (res && res.msg) || 'No se pudo cargar el ranking.';
          }
        })
        .withFailureHandler(() => {
          chefRanking.loading = false;
          chefRanking.error = 'Error de conexion cargando el ranking.';
        })
        .apiGetChefGameRanking(user.value?.email || '', chefRanking.scope, chefRanking.page);
    };

    const loadData = (targetDate = null) => {
      loading.value = true;
      // Limpiamos selección al cambiar de día o recargar
      resetSelectionState();
      almuerzo.run
        .withSuccessHandler(res => {
          loading.value = false;
          if (res.ok) {
            applyInitData(res);
          } else {
            showModal("Error cargando datos: " + res.msg, "Error");
          }
        })
        .withFailureHandler(err => {
          loading.value = false;
          showModal("Error de conexión con el servidor.", "Error de Red");
        })
        .apiGetInitData(targetDate, impersonateEmail.value);
    };

    // Cambiar de fecha (Tabs superiores)
    const changeDate = (dateStr) => {
      if (dateStr === currentDate.value) return;
      if (chefGameInCooldown.value) return;

      if (allMenus.value[dateStr]) {
        currentDate.value = dateStr;
        menu.value = allMenus.value[dateStr];
        myOrder.value = allOrders.value[dateStr] || null;
        resetSelectionState();
      } else {
        loading.value = true;
        resetSelectionState();
        almuerzo.run
          .withSuccessHandler(res => {
            loading.value = false;
            if (res.ok) {
              applyDateViewData(res);
            } else {
              showModal("Error cargando datos de la fecha: " + res.msg, "Error");
            }
          })
          .withFailureHandler(() => {
            loading.value = false;
            showModal("Error cargando el menú de la fecha seleccionada.", "Error");
          })
          .apiGetDateViewData(dateStr, impersonateEmail.value);
      }
    };

    const toggleItem = (cat, item) => {
      if (isItemDisabled(cat, item) && !selection.has(item.id)) return;

      if (selection.has(item.id)) {
        selection.delete(item.id);
        delete selectionData[item.id];
      } else {
        const catConfig = categoryById.value[cat];
        const isMulti = catConfig ? catConfig.tipo_seleccion === 'MULTIPLE' : multiSelectCats.includes(cat);

        // Single selection rule
        if (!isMulti) {
           const toRemove = [];
           selection.forEach(id => {
              if (selectionData[id] && selectionData[id].cat === cat) toRemove.push(id);
           });
           toRemove.forEach(id => {
              selection.delete(id);
              delete selectionData[id];
           });
        }

        selection.add(item.id);
        selectionData[item.id] = { cat, plato: item.plato };
      }
    };

    const changeProxyUser = () => {
       loadData(currentDate.value);
    };

    const savingReminders = ref(false);
    const toggleReminders = () => {
      if (savingReminders.value) return;
      const previous = remindersEnabled.value;
      remindersEnabled.value = !previous;
      savingReminders.value = true;
      almuerzo.run.withSuccessHandler(res => {
        savingReminders.value = false;
        if (!res.ok) { remindersEnabled.value = previous; showModal(res.msg || 'No se pudo guardar la preferencia.', 'Error'); }
      }).withFailureHandler(error => {
        savingReminders.value = false; remindersEnabled.value = previous; showModal(error.message || 'Error de conexión.', 'Error');
      }).apiSetUserPreference('reminders', remindersEnabled.value, isDeptAdmin.value ? impersonateEmail.value : null);
    };

    const toggleAdminView = () => {
       stopChefGame();
       if (viewMode.value !== 'ADMIN') {
          viewMode.value = 'ADMIN';
          loadAdminData();
       } else {
          viewMode.value = 'USER';
          nextTick(() => scheduleChefGameStart(false));
       }
    };

    const parseResponsibleEmailsValue = (value) => {
       if (typeof value !== 'string') return value || [];
       const trimmed = value.trim();
       if (!trimmed) return [];
       try {
          return JSON.parse(trimmed);
       } catch(e) {
          return trimmed;
       }
    };

    const collectResponsibleEmailCandidates = (value) => {
       const result = [];
       const collect = (item) => {
          if (!item) return;
          if (typeof item === 'string') {
             item.split(/[;,]/)
               .map(email => email.trim().toLowerCase())
               .filter(email => email)
               .forEach(email => result.push(email));
             return;
          }
          if (Array.isArray(item)) {
             item.forEach(collect);
             return;
          }
          if (typeof item === 'object') {
             const directEmail = item.email || item.mail;
             if (directEmail) {
                collect(directEmail);
                return;
             }
             if (item.emails) {
                collect(item.emails);
                return;
             }
             Object.keys(item).forEach(key => {
                const nested = item[key];
                if (Array.isArray(nested) || (nested && typeof nested === 'object')) collect(nested);
             });
          }
       };
       collect(parseResponsibleEmailsValue(value));
       return result;
    };

    const isValidResponsibleEmail = (email) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(email || '').trim());

    const normalizeResponsibleEmails = (value, options = {}) => {
       const seen = {};
       const valid = [];
       const invalid = [];

       collectResponsibleEmailCandidates(value).forEach(email => {
          if (!isValidResponsibleEmail(email)) {
             invalid.push(email);
             return;
          }
          if (!seen[email]) {
             seen[email] = true;
             valid.push(email);
          }
       });

       if (invalid.length && options.throwOnInvalid) {
          const uniqueInvalid = [...new Set(invalid)];
          throw new Error(`Corrige los correos de responsables del resumen diario: ${uniqueInvalid.join(', ')}`);
       }

       return valid;
    };

    const loadAdminData = (force = false, options = {}) => {
       if (adminState.loading && !options.allowWhileLoading) return;
       if (!force && adminState.users.length > 0) return;
       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if(res.ok) {
               adminState.users = res.users;
               adminState.orders = res.orders;
               adminState.departments = res.departments || [];
               adminState.config = res.config || {};
                Object.assign(configDraft, adminState.config);
               adminState.configList = (res.configList || []).filter(x => !x.key.startsWith('PROVIDER_'));
               adminState.configKeys = res.configKeys || [];
               adminState.holidays = res.holidays || [];
               adminState.menuCategories = res.menuCategories || [];
               if (res.menuCategories) menuCategories.value = res.menuCategories;

               adminState.responsiblesList = normalizeResponsibleEmails(adminState.config['RESPONSIBLES_EMAILS_JSON']);

               // Also populate dept map from list
               const map = {};
               adminState.departments.forEach(d => map[d.id] = d.displayName || d.nombre);
               adminState.deptMap = map;
            } else {
               if (!options.silent) showModal(res.msg, "Error");
            }
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            if (!options.silent) showModal(err.message || "Error cargando el panel administrativo.", "Error");
         })
         .apiGetAdminData();
    };

    const reloadAdminDataAfterAction = () => {
       loadAdminData(true, { allowWhileLoading: true });
    };

    // Admin Actions
    const directoryLinkSaving = ref(false);
    const resetDirectoryLink = () => {
      if (directoryLinkSaving.value) return;
      showModal('La sesión de este colaborador se cerrará. El próximo login vinculará la cuenta AD cuyo código verificado coincida. Verifica primero el cambio con TI.', 'Restablecer vínculo AD', 'CONFIRM', () => {
        directoryLinkSaving.value = true;
        almuerzo.run.withSuccessHandler(res => {
          directoryLinkSaving.value = false;
          if (!res.ok) { showModal(res.msg, 'Error'); return; }
          adminState.userModal.open = false;
          if (adminState.userModal.data.email === user.value.email) { window.location.href = '/Auth/Login'; return; }
          loadAdminData(true);
        }).withFailureHandler(err => { directoryLinkSaving.value = false; showModal(err.message || 'No se pudo restablecer el vínculo.', 'Error'); }).apiResetDirectoryLink(adminState.userModal.data.email);
      });
    };
    const openUserModal = (u = null) => {
       if (!isAdminGen.value || !u) return;
       adminState.userModal.isNew = !u;

       // CORRECCIÓN: Usamos departamentoId si existe (u), o el ID del usuario activo si es nuevo
       adminState.userModal.data = u
          ? { ...u, departamento: u.departamentoId }
          : { email: '', nombre: '', codigo: '', departamento: activeUser.value.departamentoId || '', rol: 'USUARIO', estado: 'ACTIVO' };

       adminState.userModal.open = true;
    };

    const saveUser = () => {
       if (!isAdminGen.value) return;

       adminState.loading = true;
       almuerzo.run
          .withSuccessHandler(res => {
             if(res.ok) {
                adminState.userModal.open = false;
                reloadAdminDataAfterAction();
             } else {
                adminState.loading = false;
                showModal(res.msg, "Error");
             }
          })
          .withFailureHandler(err => {
             adminState.loading = false;
             showModal(err.message || "Error guardando el usuario.", "Error");
          })
          .apiAdminSaveUser(adminState.userModal.data);
    };

    const toggleUserStatus = (u) => {
       if (!isAdminGen.value || userStatusSaving.value) return;
       const nextState = u.estado === 'ACTIVO' ? 'INACTIVO' : 'ACTIVO';
       showModal(`¿Cambiar el estado general de ${u.nombre} a ${nextState}? El bloqueo regional se conserva.`, 'Habilitación general', 'CONFIRM', () => {
          userStatusSaving.value = u.email;
          almuerzo.run.withSuccessHandler(res => { userStatusSaving.value = ''; if (res.ok) loadAdminData(true); else showModal(res.msg, 'Error'); })
            .withFailureHandler(err => { userStatusSaving.value = ''; showModal(err.message || 'No se pudo cambiar el estado.', 'Error'); })
            .apiAdminSetUserStatus({email:u.email,estado:nextState});
       });
    };

    const toggleRegionalBlock = (u) => {
       if (!canBlockUser(u) || regionalBlockSaving.value) return;
       const blocked = !u.regionalBlocked;
       showModal(`${blocked ? 'Suspender el acceso regional' : 'Retirar la suspensión regional'} de ${u.nombre}. El estado general, los roles, la identidad y los pedidos se conservan.`, 'Acceso regional', 'CONFIRM', () => {
          regionalBlockSaving.value = u.email;
          almuerzo.run.withSuccessHandler(res => { regionalBlockSaving.value = ''; if (res.ok) loadAdminData(true); else showModal(res.msg, 'Error'); })
            .withFailureHandler(err => { regionalBlockSaving.value = ''; showModal(err.message || 'No se pudo gestionar el bloqueo.', 'Error'); })
            .apiSetRegionalUserBlock({email:u.email,blocked});
       });
    };

    const adminCancelOrder = (orderId) => {
       showModal("¿Estás seguro de cancelar este pedido?", "Cancelar Pedido", "CONFIRM", () => {
           adminState.loading = true;
           almuerzo.run.withSuccessHandler(res => {
              if(res.ok) reloadAdminDataAfterAction();
              else { showModal(res.msg, "Error"); adminState.loading = false; }
           }).withFailureHandler(err => {
              adminState.loading = false;
              showModal(err.message || "Error cancelando el pedido.", "Error");
           }).apiAdminCancelOrder(orderId);
        });
    };

    // Department Actions
    const openDeptModal = (d = null) => {
       adminState.deptModal.isNew = !d;
       adminState.deptModal.data = d ? { ...d, nombre: d.displayName || d.nombre, alias: '' } : { nombre: '', alias: '', admins: '', estado: 'ACTIVO' };
       adminState.deptModal.open = true;
    };

    const saveDept = () => {
       if (adminState.deptModal.saving) return;
       if(!adminState.deptModal.data.nombre) return showModal("El nombre del grupo de almuerzo es obligatorio.");
       adminState.deptModal.saving = true;
       almuerzo.run.withSuccessHandler(res => {
          adminState.deptModal.saving = false;
           if(res.ok) {
              const d = res.department; const index = adminState.departments.findIndex(x => x.id === d.id);
              if (index < 0) adminState.departments.push(d); else adminState.departments.splice(index, 1, d);
              adminState.deptMap[d.id] = d.displayName;
              const roles = new Map((res.users || []).map(u => [u.email, u.rol]));
              adminState.users.forEach(u => { if (u.departamentoId === d.id) u.departamento = d.displayName; if (roles.has(u.email)) u.rol = roles.get(u.email); });
              adminState.orders.forEach(o => { if (o.deptId === d.id) o.dept = d.displayName; });
              if (user.value.departamentoId === d.id) user.value.departamento = d.displayName;
              if (activeUser.value.departamentoId === d.id) activeUser.value.departamento = d.displayName;
              adminState.deptModal.open = false;
           }
          else showModal(res.msg, "Error");
       }).withFailureHandler(err => { adminState.deptModal.saving = false; showModal(err.message || 'Error guardando el departamento.', 'Error'); }).apiSaveDepartment(adminState.deptModal.data);
    };

    const deleteDept = (id) => {
       showModal("¿Eliminar departamento? Esta acción no se puede deshacer.", "Eliminar Depto", "CONFIRM", () => {
           adminState.loading = true;
           almuerzo.run
              .withSuccessHandler(res => {
                 if(res.ok) {
                    reloadAdminDataAfterAction();
                 } else {
                    adminState.loading = false;
                    showModal(res.msg, "Error");
                 }
              })
              .withFailureHandler(err => {
                 adminState.loading = false;
                 showModal(err.message || "Error eliminando el departamento.", "Error");
              })
              .apiDeleteDepartment(id);
       });
    };

    // Config Actions
    const saveConfig = (section = configPanel.section) => {
      if (configPanel.saving || !configKeysBySection[section]) return;
      const payload = {};
      for (const key of configKeysBySection[section]) payload[key] = String(configDraft[key] ?? '');
      if (section === 'mail') {
        try { payload.RESPONSIBLES_EMAILS_JSON = JSON.stringify(normalizeResponsibleEmails(adminState.responsiblesList, { throwOnInvalid: true })); }
        catch (error) { configPanel.error = error.message; return; }
      }
      configPanel.saving = section; configPanel.message = ''; configPanel.error = '';
      almuerzo.run.withSuccessHandler(res => {
        configPanel.saving = '';
        if (!res.ok) { configPanel.error = res.msg || 'No se pudo guardar.'; return; }
        Object.assign(adminState.config, payload); Object.assign(configDraft, payload);
        if (res.mealPricing) mealPricing.value = res.mealPricing;
        configPanel.message = 'Cambios guardados.';
      }).withFailureHandler(error => { configPanel.saving = ''; configPanel.error = error.message || 'No se pudo guardar. Reintenta.'; }).apiSaveConfig(payload);
    };
    const cancelConfig = () => {
      for (const key of configKeysBySection[configPanel.section] || []) configDraft[key] = adminState.config[key] ?? '';
      adminState.responsiblesList = normalizeResponsibleEmails(adminState.config.RESPONSIBLES_EMAILS_JSON);
      configPanel.error = ''; configPanel.message = '';
    };

    const runTestDailyCloseEmails = () => {
       if (!isTestEmailMode.value) return;
       if (!String(adminState.config['TEST_EMAIL_DEST'] || '').trim()) {
          showModal("Guarda un destinatario de prueba antes de enviar.", "Modo prueba");
          return;
       }

       showModal("Se enviaran los correos de cierre en modo prueba. No se guardaran respaldos ni se ejecutara mantenimiento.", "Enviar prueba", "CONFIRM", () => {
           configPanel.testing = true;
          almuerzo.run
            .withSuccessHandler(res => {
                configPanel.testing = false;
                if (res && res.ok) {
                   showModal(res.msg || "Prueba encolada correctamente.", "Modo prueba");
               } else {
                  showModal((res && res.msg) || "No se pudo enviar la prueba.", "Error");
               }
            })
            .withFailureHandler(err => {
                configPanel.testing = false;
                showModal(err.message || "Error enviando la prueba.", "Error");
            })
            .apiSendDailyCloseEmailsTest();
       });
    };

    // Menu Actions
    const checkMenuDate = () => {
       if (!adminState.menuDate) return;
       const holiday = adminState.holidays.find(h => h.date === adminState.menuDate);
       if (holiday) { showModal(`No hay servicio de almuerzo: ${holiday.desc}.`, 'Día no laborable'); adminState.menuDate = ''; adminState.menuItems = []; return; }
       const d = new Date(adminState.menuDate + 'T12:00:00');
       const day = d.getDay();
       const now = new Date();
       now.setHours(0,0,0,0);
       const dZero = new Date(d);
       dZero.setHours(0,0,0,0);

       if (day === 0 || day === 6) {
          showModal("No se puede editar fines de semana.", "Fecha Inválida");
          adminState.menuDate = '';
          return;
       }
       if (dZero < now) {
          showModal("Solo se pueden editar fechas futuras o el día actual válido.", "Fecha Inválida");
          adminState.menuDate = '';
          return;
       }

       loadMenuDay();
    };

    const loadMenuDay = () => {
       if(!adminState.menuDate) return;
       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if(res.ok) adminState.menuItems = res.items;
            else {
               showModal(res.msg, "Error");
               adminState.menuDate = '';
               adminState.menuItems = [];
            }
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            showModal(err.message, "Error");
            adminState.menuDate = '';
            adminState.menuItems = [];
         })
         .apiGetMenuDay(adminState.menuDate);
    };

    const openMenuModal = (item = null, cat = (activeMenuCategories.value[0] || {}).id) => {
       adminState.menuModal.isNew = !item;
       adminState.menuModal.data = item ? {...item} : { cat, plato: '', desc: '' };
       adminState.menuModal.open = true;
    };

    const saveMenuItem = () => {
       if(!adminState.menuModal.data.plato) return showModal("El nombre del plato es requerido.");
       const payload = {
          ...adminState.menuModal.data,
          plato: formatMenuText(adminState.menuModal.data.plato),
          desc: formatMenuText(adminState.menuModal.data.desc)
       };
       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if(res.ok) { adminState.menuModal.open = false; loadMenuDay(); }
            else showModal(res.msg, "Error");
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            showModal(err.message, "Error");
         })
         .apiSaveMenuItem(adminState.menuDate, payload.cat, payload);
    };

    const deleteMenuItem = (id) => {
       showModal("¿Eliminar plato?", "Confirmar", "CONFIRM", () => {
           adminState.loading = true;
           almuerzo.run
             .withSuccessHandler(res => {
                adminState.loading = false;
                if(res.ok) loadMenuDay();
                else showModal(res.msg, "Error");
             })
             .withFailureHandler(err => {
                adminState.loading = false;
                showModal(err.message || "Error eliminando el plato.", "Error");
             })
             .apiDeleteMenuItem(id);
       });
    };

    const openCategoryModal = (category = null) => {
       adminState.categoryModal.isNew = !category;
       adminState.categoryModal.data = category
         ? {
             ...category,
             aliasesText: (category.aliases || []).join(', '),
             es_combinable: category.es_combinable !== false,
             combinable_con: Array.isArray(category.combinable_con) ? [...category.combinable_con] : [],
             tipo_seleccion: category.tipo_seleccion || 'UNICA'
           }
         : {
             nombre: '',
             orden: (menuCategories.value.length + 1) * 10,
             estado: 'ACTIVO',
             aliasesText: '',
             es_combinable: true,
             combinable_con: [],
             tipo_seleccion: 'UNICA'
           };
       adminState.categoryManagerModal.open = true;
       adminState.categoryManagerModal.view = 'FORM';
    };

    const openCategoryManager = () => {
       adminState.categoryManagerModal = { open: true, view: 'LIST' };
    };

    const saveMenuCategory = () => {
       const data = adminState.categoryModal.data;
       if (!String(data.nombre || '').trim()) return showModal('El nombre de la categoria es obligatorio.', 'Categorias');
       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if (!res.ok) return showModal(res.msg || 'No se pudo guardar la categoria.', 'Error');
            menuCategories.value = res.categories || menuCategories.value;
            adminState.menuCategories = res.categories || adminState.menuCategories;
            adminState.categoryManagerModal.view = 'LIST';
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            showModal(err.message || 'Error guardando la categoria.', 'Error');
         })
         .apiSaveMenuCategory(data);
    };

    // Menu Import Logic
    const openMenuImportModal = () => {
       adminState.menuImportModal = { open: true, step: 1, weekStart: '', text: '', preview: [], error: null };
    };

    const parseTsvRows = (raw) => {
       const rows = [];
       let currentRow = [];
       let currentCell = '';
       let insideQuotes = false;

       for (let i = 0; i < raw.length; i++) {
          const char = raw[i];
          const nextChar = raw[i + 1];

          if (char === '"') {
             if (insideQuotes && nextChar === '"') {
                currentCell += '"';
                i++;
             } else {
                insideQuotes = !insideQuotes;
             }
          } else if (char === '\t' && !insideQuotes) {
             currentRow.push(currentCell.trim());
             currentCell = '';
          } else if ((char === '\r' || char === '\n') && !insideQuotes) {
             if (char === '\r' && nextChar === '\n') i++;
             currentRow.push(currentCell.trim());
             if (currentRow.some(c => c !== '')) rows.push(currentRow);
             currentRow = [];
             currentCell = '';
          } else {
             if (insideQuotes && (char === '\n' || char === '\r')) {
                if (char === '\r' && nextChar === '\n') i++;
                if (currentCell.length > 0 && currentCell[currentCell.length - 1] !== ' ') {
                   currentCell += ' ';
                }
             } else {
                currentCell += char;
             }
          }
       }

       currentRow.push(currentCell.trim());
       if (currentRow.some(c => c !== '')) rows.push(currentRow);

       return rows.map(row => row.map(cell => {
          let c = String(cell || '').trim();
          if (c.startsWith('"') && c.endsWith('"') && c.length >= 2) {
             c = c.slice(1, -1).trim();
          }
          if (c === '"' || c === '""') c = '';
          return c;
       }));
    };

    const processMenuImport = () => {
       const text = adminState.menuImportModal.text;
       const weekStart = adminState.menuImportModal.weekStart;

       if(!weekStart) {
          adminState.menuImportModal.error = "Selecciona la fecha de inicio (Lunes).";
          return;
       }
       if(!text.trim()) {
          adminState.menuImportModal.error = "Pega el contenido del Excel.";
          return;
       }

       // 1. Calculate Working Days
       const startDate = new Date(weekStart + 'T12:00:00');
       if(startDate.getDay() !== 1) {
          adminState.menuImportModal.error = "La fecha de inicio debe ser Lunes.";
          return;
       }

       const workingDays = [];
       const holidaySet = new Set(adminState.holidays.map(h => h.date));

       // Check Mon-Fri
       for(let i=0; i<5; i++) {
          const d = new Date(startDate);
          d.setDate(d.getDate() + i);
          // Format YYYY-MM-DD manually to avoid timezone issues with toISOString
          const year = d.getFullYear();
          const month = String(d.getMonth() + 1).padStart(2, '0');
          const day = String(d.getDate()).padStart(2, '0');
          const dStr = `${year}-${month}-${day}`;

          if(!holidaySet.has(dStr)) {
             workingDays.push({ date: dStr, label: ['Lunes','Martes','Miércoles','Jueves','Viernes'][i] });
          }
       }

       // 2. Parse Text using Robust TSV Parser
       const parsedRows = parseTsvRows(text);
       const normalizeCategoryLookupKey = (value) => String(value || '')
          .normalize('NFD')
          .replace(/[\u0300-\u036f]/g, '')
          .toUpperCase()
          .replace(/[^A-Z0-9]+/g, ' ')
          .trim();
       const catMap = {};
       activeMenuCategories.value.forEach(category => {
          catMap[normalizeCategoryLookupKey(category.nombre)] = category.id;
          catMap[normalizeCategoryLookupKey(category.id)] = category.id;
          (category.aliases || []).forEach(alias => { catMap[normalizeCategoryLookupKey(alias)] = category.id; });
       });
       const knownCats = Object.keys(catMap);

       let currentCat = null;
       const columns = {}; // Idx -> { Cat -> [items] }

       parsedRows.forEach(cells => {
          if(!cells || cells.length === 0 || !cells.some(c => c)) return;

          // Detect Category Header
          const firstCellKey = normalizeCategoryLookupKey(cells[0]);
          const secondCellKey = cells.length > 1 ? normalizeCategoryLookupKey(cells[1]) : '';
          const match = knownCats.find(k => k === firstCellKey || k === secondCellKey);

          if(match) {
             currentCat = catMap[match];
             return;
          }

          if(currentCat) {
             cells.forEach((cell, idx) => {
                const cleaned = String(cell || '').trim();
                if(cleaned && !['N/A', 'n/a', 'N/a', '"', '""'].includes(cleaned)) {
                   if(!columns[idx]) columns[idx] = {};
                   if(!columns[idx][currentCat]) columns[idx][currentCat] = [];
                   columns[idx][currentCat].push(formatMenuText(cleaned));
                }
             });
          }
       });

       const colIndices = Object.keys(columns).map(Number).sort((a,b)=>a-b);
       const numCols = colIndices.length > 0 ? (colIndices[colIndices.length-1] + 1) : 0;

       // Validation Rule
       let dateMapping = [];

       if (numCols === 5) {
          // Map 0->Mon ... 4->Fri
          for(let i=0; i<5; i++) {
             const d = new Date(startDate);
             d.setDate(d.getDate() + i);
             const year = d.getFullYear();
             const month = String(d.getMonth() + 1).padStart(2, '0');
             const day = String(d.getDate()).padStart(2, '0');
             const dStr = `${year}-${month}-${day}`;

             if(!holidaySet.has(dStr)) {
                if(columns[i]) dateMapping.push({ date: dStr, items: columns[i] });
             }
          }
       } else if (numCols === workingDays.length) {
          // Map sequentially
          workingDays.forEach((wd, idx) => {
             if(columns[idx]) dateMapping.push({ date: wd.date, items: columns[idx] });
          });
       } else {
          adminState.menuImportModal.error = `Columnas detectadas: ${numCols}. Se esperaban 5 (Semana completa) o ${workingDays.length} (Días laborables).`;
          return;
       }

       if (dateMapping.length === 0) {
          adminState.menuImportModal.error = "No se encontraron platos válidos.";
          return;
       }

       adminState.menuImportModal.preview = dateMapping;
       adminState.menuImportModal.step = 2;
       adminState.menuImportModal.error = null;
    };

    const saveWeeklyMenu = () => {
       const data = {};
       adminState.menuImportModal.preview.forEach(day => {
          const items = [];
          Object.keys(day.items).forEach(cat => {
             day.items[cat].forEach(plato => {
                items.push({ cat, plato: formatMenuText(plato) });
             });
          });
          data[day.date] = items;
       });

       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if(res.ok) {
               adminState.menuImportModal.open = false;
               showModal("Menú importado exitosamente.", "Éxito");
               // Reload current view
               loadMenuDay();
               loadData();
            } else {
               showModal(res.msg, "Error");
            }
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            showModal(err.message || "Error importando el menú.", "Error");
         })
         .apiSaveWeeklyMenu(data);
    };

    // Holiday Actions
    const saveHoliday = () => {
       if(!adminState.holidayModal.data.date) return showModal("La fecha es requerida.");
       adminState.loading = true;
       almuerzo.run
         .withSuccessHandler(res => {
            adminState.loading = false;
            if(res.ok) {
               adminState.holidayModal.open = false;
               const holiday = { date: adminState.holidayModal.data.date, desc: adminState.holidayModal.data.desc, automatic: false };
               const index = adminState.holidays.findIndex(h => h.date === holiday.date);
               if (index < 0) adminState.holidays.push(holiday); else adminState.holidays.splice(index, 1, holiday);
               loadData(); // Refresh user view
            }
            else showModal(res.msg, "Error");
         })
         .withFailureHandler(err => {
            adminState.loading = false;
            showModal("Error: " + err.message, "Error");
         })
         .apiSaveHoliday(adminState.holidayModal.data.date, adminState.holidayModal.data.desc);
    };

    const deleteHoliday = (date) => {
       showModal("¿Eliminar día libre?", "Confirmar", "CONFIRM", () => {
           adminState.loading = true;
           almuerzo.run.withSuccessHandler(res => {
               adminState.loading = false;
               if(res.ok) {
                  adminState.holidays = adminState.holidays.filter(h => h.date !== date);
                  loadData();
               }
           }).apiDeleteHoliday(date);
       });
    };

    const clearFilters = (type) => {
       if(type === 'orders') {
          adminState.filters.orders = { search: '', dateStart: '', dateEnd: '', status: '', open: true };
       } else {
          adminState.filters.users.search = '';
       }
    };

    const addResponsible = () => {
       adminState.responsiblesList.push('');
    };

    const removeResponsible = (idx) => {
       adminState.responsiblesList.splice(idx, 1);
    };

    const addDeptAdmin = (u) => {
       if (!isAdminGen.value || !['USER','ADMIN_DEP','ADMIN_REG','ADMIN_GEN'].includes(u.rol) || u.regionalBlocked || u.estado !== 'ACTIVO' || u.departamentoId !== adminState.deptModal.data.id) return showModal('El responsable debe pertenecer a este grupo de almuerzo y estar habilitado.');
       const emails = (adminState.deptModal.data.admins || '').split(',').map(e=>e.trim()).filter(Boolean);
       if (!emails.includes(u.email)) emails.push(u.email);
       adminState.deptModal.data.admins = emails.join(', ');
    };

    const removeDeptAdmin = (email) => {
       let current = adminState.deptModal.data.admins ? adminState.deptModal.data.admins.split(',').map(e=>e.trim()).filter(e=>e) : [];
       current = current.filter(e => e !== email);
       adminState.deptModal.data.admins = current.join(', ');
    };

    // Métodos de Aviso General
    const dismissAnnouncement = () => {
      showAnnouncementModal.value = false;
      const id = announcementConfig.value.id;
      if (id) {
        if (!currentUserPrefs.value.announcements) currentUserPrefs.value.announcements = {};
        currentUserPrefs.value.announcements[id] = (Number(currentUserPrefs.value.announcements[id]) || 0) + 1;
        almuerzo.run.apiDismissAnnouncement(id);
      }
    };

    const nextAnnouncementSlide = () => {
      const total = announcementConfig.value.slides ? announcementConfig.value.slides.length : 0;
      if (announcementSlideIndex.value < total - 1) {
        announcementSlideIndex.value++;
      } else {
        dismissAnnouncement();
      }
    };

    const prevAnnouncementSlide = () => {
      if (announcementSlideIndex.value > 0) {
        announcementSlideIndex.value--;
      }
    };

    const openAnnouncementConfigModal = () => {
      const cfg = announcementConfig.value || {};
      announcementEditModal.value = {
        open: true,
        enabled: cfg.enabled !== false,
        id: cfg.id || 'anuncio_v7_31_valoraciones',
        expiresOn: cfg.expiresOn || todayYmd.value,
        maxDismiss: cfg.maxDismiss || 3,
        slides: Array.isArray(cfg.slides) ? JSON.parse(JSON.stringify(cfg.slides)) : [],
        forceNewId: false,
        saving: false
      };
      if (announcementEditModal.value.slides.length === 0) {
        announcementEditModal.value.slides.push({
          badge: "¡Novedad!",
          title: "Nuevo Aviso",
          description: "Escribe aquí la descripción del aviso para los colaboradores.",
          icon: "fa-bullhorn",
          theme: "indigo"
        });
      }
    };

    const addAnnouncementSlide = () => {
      announcementEditModal.value.slides.push({
        badge: "Novedad",
        title: "Nuevo Slide",
        description: "",
        icon: "fa-circle-info",
        theme: "indigo"
      });
    };

    const removeAnnouncementSlide = (idx) => {
      if (announcementEditModal.value.slides.length > 1) {
        announcementEditModal.value.slides.splice(idx, 1);
      }
    };

    const saveAnnouncementConfig = () => {
      announcementEditModal.value.saving = true;
      almuerzo.run
        .withSuccessHandler(res => {
          announcementEditModal.value.saving = false;
          if (res.ok) {
            announcementConfig.value = res.announcementConfig;
            announcementEditModal.value.open = false;
            showModal("Aviso general guardado correctamente.", "Éxito");
          } else {
            showModal(res.msg || "Error guardando el aviso.", "Error");
          }
        })
        .withFailureHandler(err => {
          announcementEditModal.value.saving = false;
          showModal(err.message || "Error de conexión.", "Error");
        })
        .apiSaveAnnouncementConfig(announcementEditModal.value);
    };

    // Métodos de Valoración de Comidas
    const canRateMeal = (dateStr) => {
      if (!dateStr || !allOrders.value[dateStr] || allOrders.value[dateStr].estado === 'CANCELADO') return false;
      const today = todayYmd.value || new Date().toISOString().substring(0, 10);
      if (dateStr < today) return true;
      if (dateStr === today) {
        const currentH = new Date(serverTime.value || Date.now()).toLocaleString("en-US", {timeZone:"America/Santo_Domingo",hour:"numeric",hourCycle:"h23"});
        return currentH >= 12 || Number(serverHour.value) >= 12;
      }
      return false;
    };

    const openMealRatingModal = (row) => {
      const existing = userMealRatings.value[row.date] || {};
      mealRatingModal.value = {
        open: true,
        date: row.date,
        dateLabel: row.label,
        orderId: row.id || (allOrders.value[row.date] ? allOrders.value[row.date].id : ''),
        rating: existing.puntuacion || 5,
        comment: existing.comentario || '',
        dishes: existing.platos || Object.values(row.items || {}).map(formatMenuText).join(', '),
        saving: false,
        error: null
      };
    };

    const saveMealRating = () => {
      mealRatingModal.value.saving = true;
      mealRatingModal.value.error = null;
      almuerzo.run
        .withSuccessHandler(res => {
          mealRatingModal.value.saving = false;
          if (res.ok) {
            mealRatingModal.value.open = false;
            userMealRatings.value[res.rating.date] = res.rating;
            showModal("¡Gracias por calificar tu comida!", "Valoración Recibida");
          } else {
            mealRatingModal.value.error = res.msg || "Error al guardar la valoración.";
          }
        })
        .withFailureHandler(err => {
          mealRatingModal.value.saving = false;
          mealRatingModal.value.error = err.message || "Error de conexión.";
        })
        .apiSubmitMealRating({
          date: mealRatingModal.value.date,
          rating: mealRatingModal.value.rating,
          comment: mealRatingModal.value.comment
        });
    };

    // Métodos de Valoración de Proveedor
    const openProviderRatingModal = () => {
      if (activeUser.value.regionalId !== regionalId.value) return showModal('Vuelve a tu regional para enviar una valoración personal.');
      const existing = userProviderRating.value || {};
      providerRatingModal.value = {
        open: true,
        rating: existing.puntuacion || 5,
        comment: existing.comentario || '',
        version: existing.version || 1,
        saving: false,
        error: null
      };
    };

    const saveProviderRating = () => {
      providerRatingModal.value.saving = true;
      providerRatingModal.value.error = null;
      almuerzo.run
        .withSuccessHandler(res => {
          providerRatingModal.value.saving = false;
          if (res.ok) {
            providerRatingModal.value.open = false;
            userProviderRating.value = res.providerRating;
            showModal("¡Gracias por calificar al proveedor de alimentos!", "Valoración Recibida");
          } else {
            providerRatingModal.value.error = res.msg || "Error al guardar la valoración.";
          }
        })
        .withFailureHandler(err => {
          providerRatingModal.value.saving = false;
          providerRatingModal.value.error = err.message || "Error de conexión.";
        })
        .apiSubmitProviderRating({
          rating: providerRatingModal.value.rating,
          comment: providerRatingModal.value.comment
        });
    };

    const openProviderResetModal = () => {
      providerResetModal.value = {
        open: true,
        providerName: '',
        price: '',
        saving: false,
        error: null
      };
    };

    const saveProviderReset = () => {
      if (!providerResetModal.value.providerName.trim() || !(Number(providerResetModal.value.price) > 0)) {
        providerResetModal.value.error = "El proveedor y su precio son obligatorios.";
        return;
      }
      providerResetModal.value.saving = true;
      almuerzo.run
        .withSuccessHandler(res => {
          providerResetModal.value.saving = false;
          if (res.ok) {
            providerResetModal.value.open = false;
            providerInfo.value = res.providerInfo;
            mealPricing.value = res.mealPricing;
            adminState.config.MEAL_PRICE_CURRENT = String(res.mealPricing.current);
            configDraft.MEAL_PRICE_CURRENT = String(res.mealPricing.current);
            userProviderRating.value = null;
            showModal("Período de evaluación del proveedor actualizado exitosamente.", "Éxito");
            loadRatingsSummary();
          } else {
            providerResetModal.value.error = res.msg || "Error actualizando el proveedor.";
          }
        })
        .withFailureHandler(err => {
          providerResetModal.value.saving = false;
          providerResetModal.value.error = err.message || "Error de conexión.";
        })
        .apiResetProviderPeriod({
          providerName: providerResetModal.value.providerName, price: providerResetModal.value.price
        });
    };

    const loadRatingsSummary = () => {
      ratingsSummary.value.loading = true;
      almuerzo.run
        .withSuccessHandler(res => {
          ratingsSummary.value.loading = false;
          if (res.ok) {
            ratingsSummary.value.providerStats = res.providerStats;
            ratingsSummary.value.mealStats = res.mealStats;
          }
        })
        .withFailureHandler(() => {
          ratingsSummary.value.loading = false;
        })
        .apiGetRatingsSummary();
    };

    const openSatisfactionCommunityModal = (tab = 'PROVIDER') => {
      satisfactionCommunityModal.value.activeTab = tab;
      satisfactionCommunityModal.value.open = true;
      satisfactionCommunityModal.value.loading = true;
      almuerzo.run
        .withSuccessHandler(res => {
          satisfactionCommunityModal.value.loading = false;
          if (res.ok) {
            satisfactionCommunityModal.value.data = res;
          } else {
            showModal(res.msg || 'No se pudieron cargar las métricas de satisfacción.', 'Satisfacción');
          }
        })
        .withFailureHandler(err => {
          satisfactionCommunityModal.value.loading = false;
          showModal(err.message || 'Error de conexión al cargar satisfacción.', 'Error');
        })
        .apiGetRatingsSummary();
    };

    const submitOrder = () => {
      if (hasBlockingErrors.value) return;

      submitting.value = true;

      // Preparamos el objeto para enviar
      const payload = {
        date: currentDate.value,
        orderId: myOrder.value?.id || '',
        impersonateEmail: impersonateEmail.value,
        categorias: Array.from(selection).map(id => selectionData[id].cat),
        items: Array.from(selection).map(id => selectionData[id].plato),
        comentarios: comments.value
      };

      almuerzo.run
        .withSuccessHandler(res => {
          submitting.value = false;
          if (res.ok) {
            const nextOrder = res.order || {
              id: myOrder.value?.id || String(Date.now()),
              resumen: payload.items.join(', '),
              detalle: { categorias: payload.categorias, items: payload.items, comentarios: payload.comentarios }
            };
            allOrders.value = { ...allOrders.value, [currentDate.value]: nextOrder };
            myOrder.value = nextOrder;
            if (res.adminData) adminData.value = res.adminData;
            resetSelectionState();
            nextTick(() => scheduleChefGameStart(true));
          } else {
            showModal("Error: " + res.msg, "Error");
          }
        })
        .withFailureHandler(() => {
          submitting.value = false;
          showModal("Error enviando el pedido. Intenta nuevamente.", "Error");
        })
        .apiSubmitOrder(payload);
    };

    const cancelOrder = () => {
      if (!myOrder.value) return;
      showModal("¿Estás seguro de cancelar este pedido?", "Cancelar Pedido", "CONFIRM", () => {
          submitting.value = true;
          almuerzo.run
            .withSuccessHandler(res => {
               submitting.value = false;
               if (res.ok) {
                 const nextOrders = { ...allOrders.value };
                 delete nextOrders[currentDate.value];
                 allOrders.value = nextOrders;
                 myOrder.value = null;
                 if (res.adminData) adminData.value = res.adminData;
                 resetSelectionState();
                 stopChefGame({ clearKey: true });
               } else {
                  showModal(res.msg, "Error");
               }
            })
            .withFailureHandler(() => {
              submitting.value = false;
              showModal("Error al cancelar.", "Error");
            })
            .apiCancelOrder(myOrder.value.id);
      });
    };

    const triggerLogoAnimation = () => {
       if (logoAnimating.value) return;

       bubbleVisible.value = false;
       if (bubbleTimer.value) clearTimeout(bubbleTimer.value);

       logoAnimating.value = true;
       setTimeout(() => {
          logoAnimating.value = false;

          const texts = ["¡Buen provecho!", "¡Que lo disfrutes!", "¡Excelente elección!", "¡Ñam ñam!", "¡Salud!"];
          bubbleText.value = texts[Math.floor(Math.random() * texts.length)];
          bubbleVisible.value = true;

          bubbleTimer.value = setTimeout(() => {
             bubbleVisible.value = false;
          }, 4000);
       }, 500);
    };

    // UI Helpers
    const formatCatName = (c) => {
      const category = categoryById.value[c];
      return category ? category.nombre : String(c || '').replace(/_/g, ' ');
    };

    const hasSelectionInCat = (cat) => selectedCats.value.includes(cat);

    const isSelected = (id) => selection.has(id);

    const scrollToCat = (cat) => {
      const el = document.getElementById('cat-'+cat);
      if(el) {
        // Offset for sticky header
        const y = el.getBoundingClientRect().top + window.pageYOffset - 140;
        window.scrollTo({top: y, behavior: 'smooth'});
      }
    };

    const getIconForCategory = (cat) => {
      const map = {
        'Arroces': 'fa-bowl-rice',
        'Granos': 'fa-spoon', // Generic
        'Carnes': 'fa-drumstick-bite',
        'Ensaladas': 'fa-carrot',
        'Viveres': 'fa-utensils',
        'Vegetariana': 'fa-leaf',
        'Caldo': 'fa-mug-hot',
        'Opcion_Rapida': 'fa-burger'
      };
      return 'fa-solid ' + (map[categoryKey(cat)] || 'fa-circle');
    };

    const formatConfigValue = (key, value) => {
       if (!value) return '';
       if (key.includes('HORA')) {
          if (String(value).match(/^\d{1,2}:\d{2}$/)) {
             const [h, m] = value.split(':');
             const date = new Date();
             date.setHours(h, m);
             return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
          }
       }
       if (key === 'MEAL_PRICE_CURRENT') {
          return formatCurrency(value);
       }
       if (key.includes('MINUTOS')) {
          return value + ' min';
       }
       return value;
    };

    const initSummaryDates = () => {
       const today = new Date();
       const next = new Date(today);
       next.setDate(today.getDate() + 7);

       const toYMD = (d) => {
          return d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0');
       };
       summaryDateStart.value = toYMD(today);
       summaryDateEnd.value = toYMD(next);
    };

    const resetSummaryDates = () => {
       initSummaryDates();
    };

    // Inicialización
    watch(
      () => [viewMode.value, chefGameCardVisible.value ? chefGameCardKey.value : ''],
      () => {
        if (viewMode.value === 'USER' && chefGameCardVisible.value) {
          nextTick(() => scheduleChefGameStart(false));
        } else {
          stopChefGame();
        }
      },
      { flush: 'post' }
    );

    onMounted(() => {
      initSummaryDates();
      const initialData = window.__INITIAL_DATA__;
      if (initialData && initialData.ok) {
        applyInitData(initialData);
        loading.value = false;
        if (new URLSearchParams(window.location.search).get('admin') === '1' && isAdmin.value) { viewMode.value = 'ADMIN'; loadAdminData(true); }
      } else {
        loadData();
      }
    });

    return {
      // Estado
      csrfToken, footerText, footerSignatureUrl, faviconUrl, brandingForm, brandingSaving, brandingMessage, clearBrandingFile, selectBrandingFile, saveBranding, loading, submitting, user, menu, myOrder, adminData,
      currentDate, dates, comments,
      summaryDateStart, summaryDateEnd, activeUsersCount,
      chefGameState, chefGame, chefRanking, rushClock, configPanel, configDraft, configSections, configCloseTime, cancelConfig, calendarYear, calendarHolidays, todayYmd,

      // Computed
      isAdmin, isDeptAdmin, isAdminGen, canManageRegion, regions, regionalId, regionalName, regionalForm, regionalSaving, regionalError, saveRegion, editRegion, rush, startRush, finishRush, moveRush, isTestEmailMode, menuLoaded, categories, menuCategories, activeMenuCategories, adminMenuCategories,
      selectedCount, validationError, hasBlockingErrors, showGrainWarning,
      isDefaultSummary,
      displayDate, isCutoffPassed,
      chefScore, chefRemainingSeconds, chefGameAvailable, chefDailyProgress, chefRemainingLabel, chefGameCardVisible, chefGameCardKey, chefGameInCooldown,

      // Métodos / Reglas UI
      toggleItem, isSelected, hasSelectionInCat, isCatDisabled, isItemDisabled,

      // Estado
      savingReminders, remindersEnabled, shouldScrollSummaryCards,
      activeUser, organizationManaged, adminTabs, canBlockUser, toggleRegionalBlock, regionalBlockSaving, userStatusSaving, deptUsers, impersonateEmail,
      viewMode, adminState,

      // Aviso General
      announcementConfig, showAnnouncementModal, announcementSlideIndex, announcementEditModal,
      dismissAnnouncement, nextAnnouncementSlide, prevAnnouncementSlide, openAnnouncementConfigModal,
      addAnnouncementSlide, removeAnnouncementSlide, saveAnnouncementConfig,

      // Proveedor y Valoraciones
      providerInfo, mealPricing, userMealRatings, userProviderRating, serverHour, serverTime,
      mealRatingModal, providerRatingModal, providerResetModal, ratingsSummary,
      satisfactionCommunityModal, openSatisfactionCommunityModal,
      canRateMeal, openMealRatingModal, saveMealRating,
      openProviderRatingModal, saveProviderRating,
      openProviderResetModal, saveProviderReset, loadRatingsSummary,

      // Acciones Principales
      changeDate, submitOrder, cancelOrder, toggleReminders, changeProxyUser,
      toggleAdminView, loadAdminData, openChefRanking, returnToOrders, loadChefRanking, syncChefGame,

      // Admin Actions
      directoryLinkSaving, resetDirectoryLink, openUserModal, saveUser, toggleUserStatus, adminCancelOrder,
      openDeptModal, saveDept, deleteDept, saveConfig, runTestDailyCloseEmails, clearFilters,

      // Dept Helpers
      addDeptAdmin, removeDeptAdmin, deptAdminSearch, filteredAdminUsers,

      // Menu/Holiday
      checkMenuDate, loadMenuDay, openMenuModal, saveMenuItem, deleteMenuItem, openCategoryManager, openCategoryModal, saveMenuCategory,
      saveHoliday, deleteHoliday,
      openMenuImportModal, processMenuImport, saveWeeklyMenu,

      // Responsibles
      addResponsible, removeResponsible,

      // Filtered Data
      filteredUsers, filteredOrders,

      // UI Utils
      formatCatName, scrollToCat, getIconForCategory, formatConfigValue, formatMenuText, formatCurrency,

      // Summary
      userWeeklySummary, summaryHeaders, summaryTitle, resetSummaryDates, userSummaryTotalCost,

      // Logo Animation
      logoAnimating, bubbleVisible, bubbleText, triggerLogoAnimation,

      // Generic Modal
      msgModal, closeMsgModal, confirmMsgModal
    };
  }
}).mount('#app');
