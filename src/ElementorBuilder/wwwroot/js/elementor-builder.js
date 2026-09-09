/**
 * Elementor Page Builder - Reusable module for ASP.NET Core MVC and other hosts
 */

const ELEMENTOR_DEFAULT_CONFIG = {
    contentFieldId: 'Content',
    uploadUrl: '/Elementor/Media/Upload',
    deleteUrl: '/Elementor/Media/Delete',
    draftKey: 'elementor-content-draft',
    cssPath: '/_content/ElementorBuilder/css/elementor-builder.css',
    contentCssPath: '/_content/ElementorBuilder/css/elementor-content.css',
    fontAwesomePath: '/lib/fontawesome/6.4.0/css/all.min.css',
    maxUploadSizeMb: 5
};

const ELEMENTOR_LAYOUT_PRESETS = [
    { id: 'full', name: 'تک ستون', icon: 'fa-square', widths: [100] },
    { id: 'half-half', name: 'دو ستون مساوی', icon: 'fa-columns', widths: [50, 50] },
    { id: 'two-thirds-one-third', name: 'محتوا + سایدبار', icon: 'fa-layout', widths: [66.666, 33.333] },
    { id: 'one-third-two-thirds', name: 'سایدبار + محتوا', icon: 'fa-table-columns', widths: [33.333, 66.666] },
    { id: 'quarter-three-quarters', name: '۱/۴ + ۳/۴', icon: 'fa-grip-lines-vertical', widths: [25, 75] },
    { id: 'third-third-third', name: 'سه ستون', icon: 'fa-grip-horizontal', widths: [33.333, 33.333, 33.333] }
];

const ELEMENTOR_SECTION_TEMPLATES = [
    {
        id: 'content-sidebar',
        name: 'متن + تصویر | سایدبار',
        presetId: 'two-thirds-one-third',
        widgets: [
            [{ type: 'heading' }, { type: 'text' }, { type: 'image' }],
            [{ type: 'list' }, { type: 'button' }]
        ]
    },
    {
        id: 'hero-block',
        name: 'بلوک معرفی',
        presetId: 'full',
        widgets: [[{ type: 'heading' }, { type: 'text' }, { type: 'button' }]]
    }
];

const ELEMENTOR_INLINE_TYPES = new Set(['text', 'heading', 'quote', 'alert']);

const ELEMENTOR_QUILL_SIZE_MIN = 10;
const ELEMENTOR_QUILL_SIZE_MAX = 72;
const ELEMENTOR_QUILL_SIZES = Array.from(
    { length: ELEMENTOR_QUILL_SIZE_MAX - ELEMENTOR_QUILL_SIZE_MIN + 1 },
    (_, i) => `${ELEMENTOR_QUILL_SIZE_MIN + i}px`
);

const ELEMENTOR_QUILL_TOOLBAR = [
    // Header + size use custom RTL controls (see enhanceRteToolbar)
    ['bold', 'italic', 'underline', 'strike'],
    [{ color: [] }, { background: [] }],
    [{ script: 'sub' }, { script: 'super' }],
    [{ list: 'ordered' }, { list: 'bullet' }],
    [{ indent: '-1' }, { indent: '+1' }],
    // Separate align buttons (not a dropdown) — RTL-first order
    [{ align: 'right' }, { align: 'center' }, { align: 'left' }, { align: 'justify' }],
    [{ direction: 'rtl' }],
    ['blockquote', 'code-block'],
    ['link'],
    ['clean']
];

const ELEMENTOR_QUILL_HEADERS = [
    { value: false, label: 'عادی' },
    { value: 1, label: 'عنوان ۱' },
    { value: 2, label: 'عنوان ۲' },
    { value: 3, label: 'عنوان ۳' },
    { value: 4, label: 'عنوان ۴' }
];

function elementorEnsureQuillFormats() {
    if (typeof Quill === 'undefined' || Quill.__tabenFormatsReady) return;
    try {
        const Size = Quill.import('attributors/style/size');
        Size.whitelist = ELEMENTOR_QUILL_SIZES;
        Quill.register(Size, true);

        // Inline styles so align/direction survive RTL + custom CSS
        const AlignStyle = Quill.import('attributors/style/align');
        // Quill omits "left" (LTR default); we need it for RTL Persian content
        AlignStyle.whitelist = ['left', 'right', 'center', 'justify'];
        Quill.register(AlignStyle, true);

        const DirectionStyle = Quill.import('attributors/style/direction');
        Quill.register(DirectionStyle, true);

        Quill.__tabenFormatsReady = true;
    } catch (err) {
        console.warn('Quill format registration failed', err);
    }
}

class ElementorBuilder {
    constructor(config) {
        this.config = this.resolveConfig(config);
        this.elements = [];
        this.selectedElement = null;
        this.textEditor = null;
        this._textEditorWidgetId = null;
        this._textEditSaveTimer = null;
        this.clipboard = null;
        this.history = [];
        this.historyIndex = -1;
        this.dragPayload = null;
        this.deviceMode = 'desktop';
        
        this.widgets = [
            { id: 'heading', name: 'عنوان', icon: 'fas fa-heading' },
            { id: 'text', name: 'متن', icon: 'fas fa-font' },
            { id: 'image', name: 'تصویر', icon: 'fas fa-image' },
            { id: 'video', name: 'ویدیو', icon: 'fas fa-video' },
            { id: 'audio', name: 'صدا / پادکست', icon: 'fas fa-podcast' },
            { id: 'button', name: 'دکمه', icon: 'fas fa-hand-pointer' },
            { id: 'divider', name: 'جداکننده', icon: 'fas fa-minus' },
            { id: 'spacer', name: 'فاصله', icon: 'fas fa-arrows-alt-v' },
            { id: 'icon', name: 'آیکون', icon: 'fas fa-star' },
            { id: 'list', name: 'لیست', icon: 'fas fa-list' },
            { id: 'quote', name: 'نقل قول', icon: 'fas fa-quote-right' },
            { id: 'html', name: 'HTML', icon: 'fas fa-code' },
            { id: 'alert', name: 'هشدار', icon: 'fas fa-exclamation-triangle' }
        ];
        
        this.init();
    }

    resolveConfig(config) {
        const canvas = document.querySelector('.elementor-canvas-inner');
        const dataset = canvas ? canvas.dataset : {};
        const globalConfig = window.ElementorBuilderConfig || {};
        const merged = { ...ELEMENTOR_DEFAULT_CONFIG, ...globalConfig, ...(config || {}) };

        if (dataset.contentFieldId) merged.contentFieldId = dataset.contentFieldId;
        if (dataset.uploadUrl) merged.uploadUrl = dataset.uploadUrl;
        if (dataset.deleteUrl) merged.deleteUrl = dataset.deleteUrl;
        if (dataset.draftKey) merged.draftKey = dataset.draftKey;
        if (dataset.cssPath) merged.cssPath = dataset.cssPath;
        if (dataset.contentCssPath) merged.contentCssPath = dataset.contentCssPath;
        if (dataset.fontAwesomePath) merged.fontAwesomePath = dataset.fontAwesomePath;
        if (dataset.maxUploadMb) merged.maxUploadSizeMb = parseInt(dataset.maxUploadMb, 10);

        return merged;
    }

    getContentField() {
        return document.getElementById(this.config.contentFieldId);
    }

    getContent() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        return canvas ? canvas.innerHTML : '';
    }

    setContent(html) {
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (canvas) {
            canvas.innerHTML = html;
            this.saveState();
        }
    }

    getMaxUploadBytes() {
        return this.config.maxUploadSizeMb * 1024 * 1024;
    }

    isDndDebugEnabled() {
        if (typeof window !== 'undefined' && window.location?.search.includes('dndDebug=1')) {
            return true;
        }
        try {
            return localStorage.getItem('elementor-dnd-debug') !== '0';
        } catch {
            return true;
        }
    }

    dndLog(...args) {
        if (!this._dndDebug) return;
        console.log('[Elementor DnD]', ...args);
    }

    dndWarn(...args) {
        if (!this._dndDebug) return;
        console.warn('[Elementor DnD]', ...args);
    }

    dndAudit(label = 'audit') {
        if (!this._dndDebug) return;

        const handles = [...document.querySelectorAll('.elementor-drag-handle[data-drag-kind]')];
        const summary = {
            label,
            dragHandles: handles.length,
            byKind: handles.reduce((acc, el) => {
                const kind = el.dataset.dragKind || 'unknown';
                acc[kind] = (acc[kind] || 0) + 1;
                return acc;
            }, {}),
            buttonHandles: handles.filter(el => el.tagName === 'BUTTON').length,
            spanHandles: handles.filter(el => el.tagName === 'SPAN').length,
            draggableHandles: handles.filter(el => el.getAttribute('draggable') === 'true').length,
            pointerMoveHandles: handles.filter(el => el.closest('.elementor-canvas, .elementor-editor-host')).length,
            widgets: document.querySelectorAll('.elementor-widget').length,
            columns: document.querySelectorAll('.elementor-column').length,
            sections: document.querySelectorAll('.elementor-section').length,
            canvasInner: !!document.querySelector('.elementor-canvas-inner'),
            editorHost: !!document.querySelector('.elementor-editor-host'),
            insideForm: !!document.querySelector('.elementor-editor-host')?.closest('form')
        };

        console.groupCollapsed('[Elementor DnD] audit:', label);
        console.table(summary);
        handles.slice(0, 8).forEach((el, i) => {
            console.log(`  handle[${i}]`, {
                kind: el.dataset.dragKind,
                tag: el.tagName,
                draggable: el.getAttribute('draggable'),
                id: el.closest('[data-id]')?.dataset?.id
            });
        });
        if (handles.length > 8) console.log(`  ... +${handles.length - 8} more handles`);
        console.groupEnd();
    }

    setupDndDebugExposure() {
        window.elementorDndDebug = {
            enable: () => {
                localStorage.setItem('elementor-dnd-debug', '1');
                this._dndDebug = true;
                console.info('[Elementor DnD] debug enabled — refresh page');
            },
            disable: () => {
                localStorage.setItem('elementor-dnd-debug', '0');
                this._dndDebug = false;
                console.info('[Elementor DnD] debug disabled — refresh page');
            },
            audit: () => this.dndAudit('manual'),
            state: () => ({
                dragPayload: this.dragPayload,
                dropHandled: this._dropHandled,
                pointerRestore: !!this._dragPointerRestore
            })
        };
    }

    queryWidget(id) {
        return document.querySelector(`.elementor-widget[data-id="${id}"]`);
    }

    queryColumn(id) {
        return document.querySelector(`.elementor-column[data-id="${id}"]`);
    }

    querySection(id) {
        return document.querySelector(`.elementor-section[data-id="${id}"]`);
    }

    handleElementAction(action, id) {
        switch (action) {
            case 'edit-section': this.editSection(id); break;
            case 'duplicate-section': this.duplicateSection(id); break;
            case 'remove-section': this.removeSection(id); break;
            case 'edit-column': this.editColumn(id); break;
            case 'edit-widget': this.editWidget(id); break;
            case 'duplicate-widget': this.duplicateWidget(id); break;
            case 'remove-widget': this.removeWidget(id); break;
        }
    }

    buildSectionToolsHtml(sectionId) {
        return `
            <div class="elementor-section-tools">
                <span class="elementor-tool-btn elementor-drag-handle" data-drag-kind="section" role="button" tabindex="0" title="جابجایی بخش">
                    <i class="fas fa-grip-vertical"></i>
                </span>
                <button type="button" class="elementor-tool-btn" data-el-action="edit-section" data-el-id="${sectionId}" title="تنظیمات">
                    <i class="fas fa-cog"></i>
                </button>
                <button type="button" class="elementor-tool-btn" data-el-action="duplicate-section" data-el-id="${sectionId}" title="کپی">
                    <i class="fas fa-copy"></i>
                </button>
                <button type="button" class="elementor-tool-btn" data-el-action="remove-section" data-el-id="${sectionId}" title="حذف">
                    <i class="fas fa-trash"></i>
                </button>
            </div>`;
    }

    buildColumnHeaderHtml(colId, widthDesktop) {
        return `
            <div class="elementor-column-header">
                <span class="elementor-column-drag-bar elementor-drag-handle" data-drag-kind="column" role="button" tabindex="0" title="جابجایی ستون">
                    <i class="fas fa-grip-vertical"></i>
                    <span class="elementor-column-header-label">جابجایی ستون</span>
                    <span class="elementor-column-badge">${widthDesktop}%</span>
                </span>
                <button type="button" class="elementor-column-header-btn" data-el-action="edit-column" data-el-id="${colId}" title="تنظیمات ستون">
                    <i class="fas fa-cog"></i>
                </button>
            </div>`;
    }

    buildWidgetToolsHtml(widgetId) {
        return `
            <div class="elementor-widget-tools">
                <span class="elementor-widget-tool-btn elementor-drag-handle" data-drag-kind="widget" role="button" tabindex="0" title="جابجایی">
                    <i class="fas fa-grip-vertical"></i>
                </span>
                <button type="button" class="elementor-widget-tool-btn" data-el-action="edit-widget" data-el-id="${widgetId}" title="ویرایش">
                    <i class="fas fa-edit"></i>
                </button>
                <button type="button" class="elementor-widget-tool-btn" data-el-action="duplicate-widget" data-el-id="${widgetId}" title="کپی">
                    <i class="fas fa-copy"></i>
                </button>
                <button type="button" class="elementor-widget-tool-btn" data-el-action="remove-widget" data-el-id="${widgetId}" title="حذف">
                    <i class="fas fa-trash"></i>
                </button>
            </div>`;
    }

    buildColumnPlaceholderHtml() {
        return `
            <div class="elementor-column-placeholder">
                <i class="fas fa-plus-circle"></i>
                <p>ویجت را اینجا بکشید</p>
            </div>`;
    }

    updateElementToolIds(root, newId) {
        root.querySelectorAll('[data-el-id]').forEach(btn => {
            btn.dataset.elId = newId;
        });
    }

    ensureColumnPlaceholder(column) {
        if (!column) return;
        const widgetCount = column.querySelectorAll(':scope > .elementor-widget').length;
        if (widgetCount > 0) {
            column.querySelector('.elementor-column-placeholder')?.remove();
            return;
        }
        if (!column.querySelector('.elementor-column-placeholder')) {
            column.insertAdjacentHTML('beforeend', this.buildColumnPlaceholderHtml());
        }
    }

    ensureColumnChrome(column) {
        if (!column) return;
        const colId = column.dataset.id || this.generateId();
        column.dataset.id = colId;
        const width = column.dataset.widthDesktop || '100';

        if (!column.querySelector('.elementor-column-header .elementor-drag-handle')) {
            column.querySelector('.elementor-column-header, .elementor-column-tools')?.remove();
            column.querySelectorAll(':scope > .elementor-column-badge').forEach(el => el.remove());
            column.insertAdjacentHTML('afterbegin', this.buildColumnHeaderHtml(colId, width));
        } else {
            const badge = column.querySelector('.elementor-column-header .elementor-column-badge');
            if (badge) badge.textContent = `${width}%`;
            this.updateElementToolIds(column.querySelector('.elementor-column-header'), colId);
        }

        this.ensureColumnPlaceholder(column);
    }

    getElementUnderDrag(e) {
        const dragging = document.querySelector(
            '.elementor-widget-dragging, .elementor-section-dragging, .elementor-column-dragging'
        );
        if (!dragging) {
            return document.elementFromPoint(e.clientX, e.clientY);
        }

        const prev = dragging.style.pointerEvents;
        dragging.style.pointerEvents = 'none';
        const el = document.elementFromPoint(e.clientX, e.clientY);
        dragging.style.pointerEvents = prev;
        return el;
    }

    cleanupPointerMove() {
        if (this._dragPointerRestore) {
            this._dragPointerRestore.classList.remove(
                'elementor-widget-dragging',
                'elementor-section-dragging',
                'elementor-column-dragging'
            );
            this._dragPointerRestore = null;
        }
        document.body.classList.remove('elementor-pointer-dragging');
        this._pointerMove = null;
        this.dragPayload = null;
        this.hideDropIndicator();
    }

    resolveMoveDropTarget(e) {
        if (!this.dragPayload) return null;

        const kind = this.dragPayload.kind;
        if (kind === 'widget-move') {
            return this.resolveWidgetDropTarget(e);
        }
        if (kind === 'section-move') {
            return this.resolveSectionDropTarget(e, this.dragPayload.element);
        }
        if (kind === 'column-move') {
            return this.resolveColumnDropTarget(e, this.dragPayload.element);
        }
        return null;
    }

    applyMoveDropTarget(element, kind, target) {
        if (!target) return false;

        if (kind === 'widget-move') {
            this.insertWidgetAt(element, target);
        } else if (kind === 'section-move') {
            this.insertSectionAt(element, target);
        } else if (kind === 'column-move') {
            this.insertColumnAt(element, target);
        } else {
            return false;
        }

        this.saveState();
        return true;
    }

    setupPointerMoveDrag(scrollContainer) {
        const threshold = 5;

        const activatePointerMove = () => {
            const pm = this._pointerMove;
            if (!pm || pm.active) return;

            pm.active = true;
            this.dragPayload = { kind: `${pm.kind}-move`, element: pm.element };
            pm.element.classList.add(`elementor-${pm.kind}-dragging`);
            this._dragPointerRestore = pm.element;
            document.body.classList.add('elementor-pointer-dragging');
            this.dndLog('pointer-move START', { kind: pm.kind, id: pm.element.dataset?.id });
        };

        document.addEventListener('mousedown', (e) => {
            const handle = e.target.closest('.elementor-drag-handle[data-drag-kind]');
            if (!handle || e.button !== 0) return;
            if (!handle.closest('.elementor-canvas, .elementor-canvas-inner, .elementor-editor-host')) return;

            const kind = handle.dataset.dragKind;
            const element =
                kind === 'section' ? handle.closest('.elementor-section') :
                kind === 'column' ? handle.closest('.elementor-column') :
                kind === 'widget' ? handle.closest('.elementor-widget') :
                null;
            if (!element) return;

            e.preventDefault();
            e.stopPropagation();

            this._pointerMove = {
                kind,
                element,
                startX: e.clientX,
                startY: e.clientY,
                active: false
            };

            this.dndLog('pointer-move mousedown', {
                kind,
                id: element.dataset?.id
            });
        }, true);

        document.addEventListener('mousemove', (e) => {
            if (!this._pointerMove) return;

            if (!this._pointerMove.active) {
                const dx = e.clientX - this._pointerMove.startX;
                const dy = e.clientY - this._pointerMove.startY;
                if (Math.hypot(dx, dy) < threshold) return;
                activatePointerMove();
            }

            if (!this.dragPayload) return;

            this.autoScrollWhileDragging(e, scrollContainer);

            const target = this.resolveMoveDropTarget(e);
            if (target) this.showDropIndicator(target);
            else this.hideDropIndicator();
        });

        document.addEventListener('mouseup', (e) => {
            if (!this._pointerMove) return;

            const pm = this._pointerMove;
            if (pm.active && this.dragPayload) {
                const target = this.resolveMoveDropTarget(e);
                const moved = this.applyMoveDropTarget(pm.element, `${pm.kind}-move`, target);
                this.dndLog('pointer-move END', {
                    kind: pm.kind,
                    moved,
                    target
                });
                if (!moved) {
                    this.dndWarn('pointer-move FAIL — no valid drop target');
                }
            } else if (pm) {
                this.dndLog('pointer-move CANCEL — threshold not reached');
            }

            this.cleanupPointerMove();
        });
    }

    autoScrollWhileDragging(e, container) {
        if (!container) return;

        const rect = container.getBoundingClientRect();
        const edge = 64;
        const maxSpeed = 20;

        if (e.clientY < rect.top + edge) {
            const ratio = 1 - Math.max(0, e.clientY - rect.top) / edge;
            container.scrollTop -= Math.ceil(maxSpeed * ratio);
        } else if (e.clientY > rect.bottom - edge) {
            const ratio = 1 - Math.max(0, rect.bottom - e.clientY) / edge;
            container.scrollTop += Math.ceil(maxSpeed * ratio);
        }
    }

    syncSelection() {
        if (this.selectedElement && !document.contains(this.selectedElement)) {
            this.closeSettings();
        }
    }

    openSettingsView(html, subtitle = '') {
        const widgetsView = document.querySelector('.elementor-panel-view-widgets');
        const settingsView = document.querySelector('.elementor-panel-view-settings');
        const settingsContent = document.querySelector('.elementor-settings-content');
        const subtitleEl = document.querySelector('.elementor-settings-subtitle');
        const panel = document.querySelector('.elementor-panel');

        if (settingsContent) settingsContent.innerHTML = html;
        if (subtitleEl) subtitleEl.textContent = subtitle;

        widgetsView?.classList.add('elementor-panel-hidden');
        settingsView?.classList.remove('elementor-panel-hidden');
        panel?.classList.add('showing-settings');
    }

    removeElement(element) {
        if (!element) return;
        if (element.classList.contains('elementor-widget')) {
            this.removeWidget(element.dataset.id, false);
        } else if (element.classList.contains('elementor-section')) {
            this.removeSection(element.dataset.id, false);
        }
    }
    
    init() {
        this._dndDebug = this.isDndDebugEnabled();
        this.setupDndDebugExposure();
        if (this._dndDebug) {
            console.info('[Elementor DnD] debug ON — filter console by "Elementor DnD". Disable: elementorDndDebug.disable()');
        }

        this.setupEventListeners();
        this.setupCanvasInteractions();
        this.loadExistingContent();
        this.setupDragAndDrop();
        this.setupKeyboardShortcuts();
        this.renderWidgets();
        this.renderLayoutPresets();
        this.renderSectionTemplates();
        this.ensureAddSectionButton();
        this.dndAudit('after-init');
    }
    
    setupEventListeners() {
        // Toolbar buttons
        document.querySelector('[data-action="add-section"]')?.addEventListener('click', () => this.openLayoutsPanel());
        document.querySelector('[data-action="save"]')?.addEventListener('click', () => {
            this.save();
            this.showMessage('محتوا ذخیره شد', 'success');
        });
        document.querySelector('[data-action="preview"]')?.addEventListener('click', () => this.preview());
        document.querySelector('[data-action="undo"]')?.addEventListener('click', () => this.undo());
        document.querySelector('[data-action="redo"]')?.addEventListener('click', () => this.redo());
        document.querySelector('[data-action="clear"]')?.addEventListener('click', () => this.clear());
        document.querySelector('[data-action="fullscreen"]')?.addEventListener('click', () => this.toggleFullscreen());
        document.querySelector('[data-action="submit-form"]')?.addEventListener('click', () => this.submitParentForm());
        
        // Device mode buttons
        document.querySelectorAll('.elementor-device-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const deviceBtn = e.currentTarget.closest('.elementor-device-btn');
                if (deviceBtn) this.changeDeviceMode(deviceBtn.dataset.device);
            });
        });

        // Toggle panel
        document.querySelector('.elementor-toggle-panel')?.addEventListener('click', () => {
            document.querySelector('.elementor-panel').classList.toggle('collapsed');
        });
        
        // Settings panel back
        document.querySelector('.elementor-settings-back')?.addEventListener('click', () => {
            this.closeSettings();
        });
        
        // Tab switching
        document.querySelectorAll('.elementor-panel-tab').forEach(tab => {
            tab.addEventListener('click', (e) => {
                e.preventDefault();
                const tabBtn = e.currentTarget.closest('.elementor-panel-tab');
                if (tabBtn) this.switchTab(tabBtn.dataset.tab);
            });
        });

        document.querySelector('.elementor-editor-host')?.addEventListener('click', (e) => {
            const actionBtn = e.target.closest('[data-el-action]');
            if (!actionBtn) return;
            e.preventDefault();
            e.stopPropagation();
            this.handleElementAction(actionBtn.dataset.elAction, actionBtn.dataset.elId);
        });
    }

    setupCanvasInteractions() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (!canvas) return;

        canvas.addEventListener('click', (e) => {
            if (e.target.closest('[data-el-action], button, .elementor-drag-handle')) return;
            if (e.target.closest('.elementor-inline-text-editor, .ql-toolbar, .ql-editor, .elementor-inline-heading, .elementor-rte-dock')) return;

            const widget = e.target.closest('.elementor-widget');
            if (widget) {
                this.editWidget(widget.dataset.id);
                return;
            }

            const column = e.target.closest('.elementor-column');
            if (column && e.target.closest('.elementor-column-placeholder')) {
                this.editColumn(column.dataset.id);
                return;
            }

            const section = e.target.closest('.elementor-section');
            if (section && e.target.closest('.elementor-columns') && !e.target.closest('.elementor-widget')) {
                this.editSection(section.dataset.id);
                return;
            }

            if (e.target.closest('.elementor-add-section-btn, .elementor-empty-state')) {
                this.closeSettings();
            }
        });
    }
    
    setupKeyboardShortcuts() {
        document.addEventListener('keydown', (e) => {
            // Ctrl+Z / Cmd+Z - Undo
            if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey) {
                e.preventDefault();
                this.undo();
            }
            // Ctrl+Shift+Z / Cmd+Shift+Z - Redo
            if ((e.ctrlKey || e.metaKey) && e.key === 'z' && e.shiftKey) {
                e.preventDefault();
                this.redo();
            }
            // Ctrl+C / Cmd+C - Copy
            if ((e.ctrlKey || e.metaKey) && e.key === 'c') {
                if (this.selectedElement) {
                    e.preventDefault();
                    this.copy();
                }
            }
            // Ctrl+V / Cmd+V - Paste
            if ((e.ctrlKey || e.metaKey) && e.key === 'v') {
                if (this.clipboard) {
                    e.preventDefault();
                    this.paste();
                }
            }
            // Delete - Remove
            if (e.key === 'Delete' && this.selectedElement) {
                e.preventDefault();
                this.removeElement(this.selectedElement);
            }
            // Escape - exit fullscreen
            if (e.key === 'Escape' && document.body.classList.contains('elementor-builder-fullscreen')) {
                e.preventDefault();
                this.toggleFullscreen(false);
            }
        });
    }

    isFullscreen() {
        return document.body.classList.contains('elementor-builder-fullscreen');
    }

    toggleFullscreen(force) {
        const next = typeof force === 'boolean'
            ? force
            : !this.isFullscreen();

        document.body.classList.toggle('elementor-builder-fullscreen', next);
        document.documentElement.classList.toggle('elementor-builder-fullscreen', next);
        document.querySelector('.elementor-editor-host')?.classList.toggle('is-fullscreen', next);

        const btn = document.querySelector('[data-action="fullscreen"]');
        if (btn) {
            btn.classList.toggle('active', next);
            const icon = btn.querySelector('i');
            const label = btn.querySelector('.elementor-fullscreen-label');
            if (icon) icon.className = next ? 'fas fa-compress' : 'fas fa-expand';
            if (label) label.textContent = next ? 'خروج' : 'تمام‌صفحه';
            btn.title = next ? 'خروج از تمام‌صفحه (Esc)' : 'حالت تمام‌صفحه (Esc برای خروج)';
        }

        document.documentElement.style.overflow = next ? 'hidden' : '';
        document.body.style.overflow = next ? 'hidden' : '';

        window.dispatchEvent(new CustomEvent('elementor:fullscreenchange', { detail: { active: next } }));

        if (next) {
            this.showMessage('حالت تمام‌صفحه — Esc برای بازگشت', 'success');
        }

        window.requestAnimationFrame(() => window.dispatchEvent(new Event('resize')));
    }

    exitFullscreen() {
        if (this.isFullscreen()) {
            this.toggleFullscreen(false);
        }
    }

    submitParentForm() {
        this.save();
        const form = document.querySelector('.elementor-editor-host')?.closest('form');
        if (form && typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else if (form) {
            form.submit();
        }
    }
    
    renderWidgets() {
        const container = document.querySelector('.elementor-widgets-list');
        if (!container) return;
        
        container.innerHTML = this.widgets.map(widget => `
            <div class="elementor-panel-item elementor-widget-item" draggable="true" data-widget-type="${widget.id}">
                <i class="${widget.icon}"></i>
                <span>${widget.name}</span>
            </div>
        `).join('');
    }
    
    upgradeDragHandles() {
        const legacyButtons = document.querySelectorAll('button.elementor-drag-handle[data-drag-kind]');
        if (legacyButtons.length && this._dndDebug) {
            this.dndLog('upgradeDragHandles: converting legacy button handles', legacyButtons.length);
        }

        legacyButtons.forEach(btn => {
            const kind = btn.dataset.dragKind;
            const host =
                kind === 'section' ? btn.closest('.elementor-section') :
                kind === 'column' ? btn.closest('.elementor-column') :
                kind === 'widget' ? btn.closest('.elementor-widget') :
                null;
            if (!host) return;

            if (kind === 'section') {
                host.querySelector('.elementor-section-tools')?.remove();
                host.insertAdjacentHTML('afterbegin', this.buildSectionToolsHtml(host.dataset.id));
            } else if (kind === 'column') {
                host.querySelector('.elementor-column-header, .elementor-column-tools')?.remove();
                const width = host.dataset.widthDesktop || '100';
                host.insertAdjacentHTML('afterbegin', this.buildColumnHeaderHtml(host.dataset.id, width));
            } else if (kind === 'widget') {
                host.querySelector('.elementor-widget-tools')?.remove();
                host.insertAdjacentHTML('afterbegin', this.buildWidgetToolsHtml(host.dataset.id));
            }
        });
    }

    setupDragAndDrop() {
        const canvasInner = document.querySelector('.elementor-canvas-inner');
        const scrollContainer = document.querySelector('.elementor-canvas');
        const editorHost = document.querySelector('.elementor-editor-host');
        if (!canvasInner || !scrollContainer) {
            this.dndWarn('setupDragAndDrop ABORT — canvas not found', { canvasInner: !!canvasInner, scrollContainer: !!scrollContainer });
            return;
        }

        this.dndLog('setupDragAndDrop OK', {
            editorHost: !!editorHost,
            formId: editorHost?.closest('form')?.id || null
        });

        this._dropIndicator = null;
        this._dragPointerRestore = null;
        this._dropHandled = false;
        this._dndDragOverLogAt = 0;
        this._pointerMove = null;

        this.setupPointerMoveDrag(scrollContainer);

        document.addEventListener('dragstart', (e) => {
            const dragHandle = e.target.closest('.elementor-drag-handle[data-drag-kind]');
            if (dragHandle?.closest('.elementor-canvas, .elementor-canvas-inner, .elementor-editor-host')) {
                e.preventDefault();
                this.dndLog('dragstart blocked on canvas handle — using pointer-move');
                return;
            }

            const layoutItem = e.target.closest('.elementor-layout-preset');
            if (layoutItem) {
                this.dragPayload = {
                    kind: 'layout-new',
                    presetId: layoutItem.dataset.presetId
                };
                e.dataTransfer.effectAllowed = 'copy';
                e.dataTransfer.setData('text/elementor-layout', layoutItem.dataset.presetId);
                layoutItem.classList.add('dragging');
                e.stopPropagation();
                this.dndLog('dragstart → layout-new', { presetId: layoutItem.dataset.presetId });
                return;
            }

            if (e.target.closest('button, input, textarea, select, a')) {
                this.dndWarn('dragstart BLOCKED — interactive element', {
                    tag: e.target.tagName,
                    className: e.target.className
                });
                e.preventDefault();
                return;
            }

            const widgetItem = e.target.closest('.elementor-widget-item');
            if (widgetItem) {
                this.dragPayload = {
                    kind: 'widget-new',
                    type: widgetItem.dataset.widgetType
                };
                e.dataTransfer.effectAllowed = 'copy';
                e.dataTransfer.setData('text/elementor-widget', widgetItem.dataset.widgetType);
                widgetItem.classList.add('dragging');
                e.stopPropagation();
                this.dndLog('dragstart → widget-new', { type: widgetItem.dataset.widgetType });
            }
        });

        document.addEventListener('dragend', (e) => {
            if (this._pointerMove?.active) return;

            const hadPayload = !!this.dragPayload;
            const wasHandled = this._dropHandled;

            this.dndLog('dragend', {
                hadPayload,
                wasHandled,
                kind: this.dragPayload?.kind,
                dropEffect: e.dataTransfer?.dropEffect
            });

            if (hadPayload && !wasHandled) {
                this.dndWarn('dragend WITHOUT drop — drag cancelled or drop handler never ran');
            }

            document.querySelectorAll('.elementor-widget-item.dragging, .elementor-layout-preset.dragging').forEach(el => {
                el.classList.remove('dragging');
            });
            document.querySelectorAll('.elementor-widget.elementor-widget-dragging').forEach(el => {
                el.classList.remove('elementor-widget-dragging');
            });
            document.querySelectorAll('.elementor-section.elementor-section-dragging').forEach(el => {
                el.classList.remove('elementor-section-dragging');
            });
            document.querySelectorAll('.elementor-column.elementor-column-dragging').forEach(el => {
                el.classList.remove('elementor-column-dragging');
            });
            if (this._dragPointerRestore) {
                this._dragPointerRestore.style.pointerEvents = '';
                this._dragPointerRestore = null;
            }
            this.dragPayload = null;
            this._dropHandled = false;
            this.hideDropIndicator();
        });

        const describeHit = (e) => {
            const hit = this.getElementUnderDrag(e);
            return {
                x: e.clientX,
                y: e.clientY,
                hitTag: hit?.tagName || null,
                hitClass: hit?.className || null,
                inCanvas: !!hit?.closest('.elementor-canvas-inner'),
                inColumn: !!hit?.closest('.elementor-column'),
                inWidget: !!hit?.closest('.elementor-widget')
            };
        };

        const isOverCanvas = (e) => {
            const hit = this.getElementUnderDrag(e);
            if (hit?.closest('.elementor-canvas, .elementor-canvas-inner, .elementor-editor-host')) {
                return true;
            }
            return !!e.target?.closest?.('.elementor-canvas, .elementor-canvas-inner, .elementor-editor-host');
        };

        const acceptDrop = (e) => {
            if (!this.dragPayload) return;
            e.preventDefault();
        };

        [canvasInner, scrollContainer, editorHost].forEach(el => el?.addEventListener('dragover', acceptDrop));

        const handleDragOver = (e) => {
            if (!this.dragPayload) return;
            e.preventDefault();

            if (isOverCanvas(e)) {
                this.autoScrollWhileDragging(e, scrollContainer);
            }

            const panelContent = document.querySelector('.elementor-panel-content');
            if (panelContent && e.target.closest('.elementor-panel')) {
                this.autoScrollWhileDragging(e, panelContent);
            }

            const now = Date.now();
            if (now - this._dndDragOverLogAt > 600) {
                this._dndDragOverLogAt = now;
                this.dndLog('dragover', {
                    kind: this.dragPayload.kind,
                    overCanvas: isOverCanvas(e),
                    targetTag: e.target?.tagName,
                    ...describeHit(e)
                });
            }

            if (!isOverCanvas(e)) {
                this.hideDropIndicator();
                return;
            }

            const kind = this.dragPayload.kind;
            if (e.dataTransfer) {
                e.dataTransfer.dropEffect = kind.includes('move') ? 'move' : 'copy';
            }

            let target = null;
            if (kind === 'widget-new' || kind === 'widget-move') {
                target = this.resolveWidgetDropTarget(e);
            } else if (kind === 'layout-new' || kind === 'section-move') {
                target = this.resolveSectionDropTarget(e, this.dragPayload.element);
            } else if (kind === 'column-move') {
                target = this.resolveColumnDropTarget(e, this.dragPayload.element);
            }

            if (target) this.showDropIndicator(target);
            else this.hideDropIndicator();
        };

        document.addEventListener('dragover', handleDragOver);

        const handleDrop = (e, source = 'unknown') => {
            this.dndLog('drop event', {
                source,
                kind: this.dragPayload?.kind,
                dropHandled: this._dropHandled,
                ...describeHit(e)
            });

            if (!this.dragPayload) {
                this.dndWarn('drop SKIP — no dragPayload');
                return;
            }
            if (this._dropHandled) {
                this.dndLog('drop SKIP — already handled');
                return;
            }
            if (!isOverCanvas(e)) {
                this.dndWarn('drop SKIP — not over canvas', describeHit(e));
                return;
            }

            e.preventDefault();
            e.stopPropagation();
            this._dropHandled = true;

            const payload = this.dragPayload;
            this.hideDropIndicator();

            switch (payload.kind) {
                case 'widget-new': {
                    const target = this.resolveWidgetDropTarget(e);
                    this.dndLog('drop widget-new', { target });
                    if (target) this.addWidget(payload.type, target.column, true, target);
                    else this.dndWarn('drop widget-new FAIL — no target');
                    break;
                }
                case 'widget-move': {
                    const target = this.resolveWidgetDropTarget(e);
                    this.dndLog('drop widget-move', {
                        target,
                        widgetId: payload.element?.dataset?.id
                    });
                    if (target) {
                        this.insertWidgetAt(payload.element, target);
                        this.saveState();
                        this.dndLog('drop widget-move OK');
                    } else {
                        this.dndWarn('drop widget-move FAIL — no target');
                    }
                    break;
                }
                case 'layout-new': {
                    const target = this.resolveSectionDropTarget(e);
                    this.dndLog('drop layout-new', { target, presetId: payload.presetId });
                    if (target) this.addSectionFromPreset(payload.presetId, target);
                    else this.dndWarn('drop layout-new FAIL — no target');
                    break;
                }
                case 'section-move': {
                    const target = this.resolveSectionDropTarget(e, payload.element);
                    this.dndLog('drop section-move', { target, sectionId: payload.element?.dataset?.id });
                    if (target) {
                        this.insertSectionAt(payload.element, target);
                        this.saveState();
                        this.dndLog('drop section-move OK');
                    } else {
                        this.dndWarn('drop section-move FAIL — no target');
                    }
                    break;
                }
                case 'column-move': {
                    const target = this.resolveColumnDropTarget(e, payload.element);
                    this.dndLog('drop column-move', { target, columnId: payload.element?.dataset?.id });
                    if (target) {
                        this.insertColumnAt(payload.element, target);
                        this.saveState();
                        this.dndLog('drop column-move OK');
                    } else {
                        this.dndWarn('drop column-move FAIL — no target');
                    }
                    break;
                }
                default:
                    this.dndWarn('drop SKIP — unknown kind', payload.kind);
            }
        };

        document.addEventListener('drop', (e) => handleDrop(e, 'document-capture'), true);
    }

    resolveWidgetDropTarget(e) {
        const hit = this.getElementUnderDrag(e);
        const column = hit?.closest('.elementor-column');
        if (!column) return null;

        const widgets = Array.from(column.querySelectorAll(':scope > .elementor-widget'))
            .filter(w => !w.classList.contains('elementor-widget-dragging'));

        if (widgets.length === 0) {
            return { type: 'widget', column, reference: null, position: 'append' };
        }

        for (const widget of widgets) {
            const rect = widget.getBoundingClientRect();
            if (e.clientY < rect.top + rect.height / 2) {
                return { type: 'widget', column, reference: widget, position: 'before' };
            }
        }

        return { type: 'widget', column, reference: widgets[widgets.length - 1], position: 'after' };
    }

    resolveSectionDropTarget(e, excludeSection = null) {
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (!canvas) return null;

        const sections = Array.from(canvas.querySelectorAll(':scope > .elementor-section'))
            .filter(section => section !== excludeSection);

        if (sections.length === 0 || this.getElementUnderDrag(e)?.closest('.elementor-empty-state')) {
            return { type: 'section', reference: null, position: 'append' };
        }

        for (const section of sections) {
            const rect = section.getBoundingClientRect();
            if (e.clientY < rect.top + rect.height / 2) {
                return { type: 'section', reference: section, position: 'before' };
            }
        }

        return { type: 'section', reference: sections[sections.length - 1], position: 'after' };
    }

    resolveColumnDropTarget(e, excludeColumn = null) {
        const hit = this.getElementUnderDrag(e);
        let columnsEl = hit?.closest('.elementor-columns');

        if (!columnsEl && excludeColumn) {
            columnsEl = excludeColumn.closest('.elementor-columns');
        }
        if (!columnsEl) return null;

        if (excludeColumn) {
            const sourceSection = excludeColumn.closest('.elementor-section');
            const targetSection = columnsEl.closest('.elementor-section');
            if (sourceSection !== targetSection) return null;
        }

        const columns = Array.from(columnsEl.querySelectorAll(':scope > .elementor-column'))
            .filter(col => col !== excludeColumn);

        if (columns.length === 0) {
            return { type: 'column', columnsEl, reference: null, position: 'append' };
        }

        const sorted = columns.slice().sort((a, b) => {
            return a.getBoundingClientRect().left - b.getBoundingClientRect().left;
        });

        for (const col of sorted) {
            const rect = col.getBoundingClientRect();
            if (e.clientX < rect.left + rect.width / 2) {
                return { type: 'column', columnsEl, reference: col, position: 'before' };
            }
        }

        return { type: 'column', columnsEl, reference: sorted[sorted.length - 1], position: 'after' };
    }

    insertSectionAt(section, target) {
        const canvas = document.querySelector('.elementor-canvas-inner');
        canvas.querySelector('.elementor-empty-state')?.remove();

        const addBtn = canvas.querySelector('.elementor-add-section-btn');

        if (!target.reference || target.position === 'append') {
            if (addBtn) canvas.insertBefore(section, addBtn);
            else canvas.appendChild(section);
            return;
        }

        if (target.position === 'before') {
            canvas.insertBefore(section, target.reference);
        } else {
            canvas.insertBefore(section, target.reference.nextSibling);
        }
    }

    insertColumnAt(column, target) {
        const { columnsEl, reference, position } = target;

        if (!reference || position === 'append') {
            columnsEl.appendChild(column);
        } else if (position === 'before') {
            columnsEl.insertBefore(column, reference);
        } else {
            columnsEl.insertBefore(column, reference.nextSibling);
        }

        const section = columnsEl.closest('.elementor-section');
        if (section) this.applyResponsiveVars(section);
    }

    ensureDropIndicator() {
        if (this._dropIndicator) return;
        this._dropIndicator = document.createElement('div');
        this._dropIndicator.className = 'elementor-drop-indicator';
        this._dropIndicator.hidden = true;
    }

    showDropIndicator(target) {
        this.hideDropIndicator();
        this.ensureDropIndicator();

        if (target.type === 'section') {
            const canvas = document.querySelector('.elementor-canvas-inner');
            const canvasRect = canvas.getBoundingClientRect();
            this._dropIndicator.className = 'elementor-drop-indicator elementor-drop-indicator--section';

            let top = 12;
            if (target.reference) {
                const rect = target.reference.getBoundingClientRect();
                top = target.position === 'before'
                    ? rect.top - canvasRect.top - 2
                    : rect.bottom - canvasRect.top - 1;
            }

            this._dropIndicator.style.top = `${Math.max(8, top)}px`;
            canvas.appendChild(this._dropIndicator);
            this._dropIndicator.hidden = false;
            return;
        }

        if (target.type === 'column') {
            const { columnsEl, reference, position } = target;
            columnsEl.classList.add('drag-over');
            this._dropIndicator.className = 'elementor-drop-indicator elementor-drop-indicator--column';

            const parentRect = columnsEl.getBoundingClientRect();
            let left = 8;
            if (reference) {
                const rect = reference.getBoundingClientRect();
                left = position === 'before'
                    ? rect.left - parentRect.left - 2
                    : rect.right - parentRect.left - 1;
            }

            this._dropIndicator.style.left = `${Math.max(4, left)}px`;
            columnsEl.appendChild(this._dropIndicator);
            this._dropIndicator.hidden = false;
            return;
        }

        const { column, reference, position } = target;
        document.querySelectorAll('.elementor-column.drag-over').forEach(el => el.classList.remove('drag-over'));
        column.classList.add('drag-over');
        this._dropIndicator.className = 'elementor-drop-indicator';

        const colRect = column.getBoundingClientRect();
        let top = column.clientHeight - 6;

        if (reference) {
            const refRect = reference.getBoundingClientRect();
            top = position === 'before'
                ? refRect.top - colRect.top - 2
                : refRect.bottom - colRect.top - 1;
        } else {
            top = 8;
        }

        this._dropIndicator.style.top = `${Math.max(4, top)}px`;
        this._dropIndicator.style.left = '';
        column.appendChild(this._dropIndicator);
        this._dropIndicator.hidden = false;
    }

    hideDropIndicator() {
        document.querySelectorAll('.elementor-column.drag-over, .elementor-columns.drag-over').forEach(el => {
            el.classList.remove('drag-over');
        });
        if (this._dropIndicator) {
            this._dropIndicator.hidden = true;
            this._dropIndicator.remove();
            this._dropIndicator.className = 'elementor-drop-indicator';
        }
    }

    insertWidgetAt(widget, target) {
        const { column, reference, position } = target;
        column.querySelector('.elementor-column-placeholder')?.remove();

        if (!reference || position === 'append') {
            column.appendChild(widget);
        } else if (position === 'before') {
            column.insertBefore(widget, reference);
        } else {
            column.insertBefore(widget, reference.nextSibling);
        }

        this.ensureColumnPlaceholder(column);
    }
    
    openLayoutsPanel() {
        this.switchTab('widgets');
        const block = document.querySelector('.elementor-layout-presets-grid')?.closest('.elementor-panel-block');
        block?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        block?.classList.add('elementor-panel-block-highlight');
        setTimeout(() => block?.classList.remove('elementor-panel-block-highlight'), 1200);
    }

    getPresetById(presetId) {
        return ELEMENTOR_LAYOUT_PRESETS.find(p => p.id === presetId) || ELEMENTOR_LAYOUT_PRESETS[0];
    }

    inferSectionLayout(section) {
        const cols = section.querySelectorAll('.elementor-column');
        if (cols.length === 0) return 'full';

        const widths = Array.from(cols).map(col =>
            parseFloat(col.dataset.widthDesktop || '100')
        );

        const preset = ELEMENTOR_LAYOUT_PRESETS.find(p =>
            p.widths.length === widths.length &&
            p.widths.every((w, i) => Math.abs(w - widths[i]) < 0.5)
        );

        return preset ? preset.id : 'full';
    }

    renderLayoutPresets() {
        const grid = document.querySelector('.elementor-layout-presets-grid');
        if (!grid) return;
        grid.innerHTML = ELEMENTOR_LAYOUT_PRESETS.map(preset => `
            <button type="button" draggable="true" class="elementor-panel-item elementor-layout-preset elementor-layout-item" data-preset-id="${preset.id}" title="${preset.name} — کلیک یا بکشید روی بوم">
                <div class="elementor-layout-preset-bars">
                    ${preset.widths.map(w => `<span style="flex:${w}"></span>`).join('')}
                </div>
                <span>${preset.name}</span>
            </button>
        `).join('');
        grid.querySelectorAll('.elementor-layout-preset').forEach(btn => {
            btn.addEventListener('click', (e) => {
                if (btn.classList.contains('dragging')) return;
                e.preventDefault();
                e.stopPropagation();
                this.addSectionFromPreset(btn.dataset.presetId);
            });
        });
    }

    renderSectionTemplates() {
        const grid = document.querySelector('.elementor-section-templates-list');
        if (!grid) return;
        grid.innerHTML = ELEMENTOR_SECTION_TEMPLATES.map(t => `
            <button type="button" class="elementor-panel-item elementor-template-item" data-template-id="${t.id}">
                <i class="fas fa-layer-group"></i>
                <span>${t.name}</span>
            </button>
        `).join('');
        grid.querySelectorAll('.elementor-template-item').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.addSectionTemplate(btn.dataset.templateId);
            });
        });
    }

    addSection(presetId = 'full') {
        this.addSectionFromPreset(presetId);
    }

    addSectionFromPreset(presetId, dropTarget = null) {
        const preset = this.getPresetById(presetId);
        const canvas = document.querySelector('.elementor-canvas-inner');
        const emptyState = canvas.querySelector('.elementor-empty-state');
        if (emptyState) emptyState.remove();

        const sectionId = this.generateId();
        const section = document.createElement('div');
        section.className = 'elementor-section';
        section.dataset.id = sectionId;
        section.dataset.layout = preset.id;
        section.dataset.paddingTop = section.dataset.paddingTop || '20';
        section.dataset.paddingBottom = section.dataset.paddingBottom || '20';
        section.dataset.gap = section.dataset.gap || '20';
        section.dataset.reverseMobile = section.dataset.reverseMobile || '0';

        section.innerHTML = `
            ${this.buildSectionToolsHtml(sectionId)}
            <div class="elementor-columns">
                ${this.generateColumnsFromPreset(preset)}
            </div>
        `;

        if (dropTarget) {
            this.insertSectionAt(section, dropTarget);
        } else {
            const addBtn = canvas.querySelector('.elementor-add-section-btn');
            if (addBtn) {
                canvas.insertBefore(section, addBtn);
            } else {
                canvas.appendChild(section);
            }
        }

        this.applySectionStyles(section);
        this.applyResponsiveVars(section);
        this.applyDevicePreview();
        this.ensureAddSectionButton();
        this.saveState();
        this.showMessage('بخش جدید اضافه شد', 'success');
    }

    addSectionTemplate(templateId) {
        const template = ELEMENTOR_SECTION_TEMPLATES.find(t => t.id === templateId);
        if (!template) return;

        this.addSectionFromPreset(template.presetId);
        const canvas = document.querySelector('.elementor-canvas-inner');
        const section = canvas.querySelector('.elementor-section:last-of-type');
        if (!section) return;

        const columns = section.querySelectorAll('.elementor-column');
        template.widgets.forEach((colWidgets, index) => {
            const column = columns[index];
            if (!column) return;
            colWidgets.forEach(w => this.addWidget(w.type, column, false));
        });

        this.applyResponsiveVars(section);
        this.saveState();
        this.showMessage('قالب بخش اضافه شد', 'success');
    }

    generateColumnsFromPreset(preset) {
        return preset.widths.map(widthDesktop => {
            const colId = this.generateId();
            const widthTablet = preset.widths.length > 1 ? 100 : widthDesktop;
            const widthMobile = preset.widths.length > 1 ? 100 : widthDesktop;
            return this.generateColumnHtml(colId, widthDesktop, widthTablet, widthMobile);
        }).join('');
    }

    generateColumnHtml(colId, widthDesktop, widthTablet, widthMobile) {
        return `
            <div class="elementor-column"
                 data-id="${colId}"
                 data-width-desktop="${widthDesktop}"
                 data-width-tablet="${widthTablet}"
                 data-width-mobile="${widthMobile}"
                 data-hide-tablet="0"
                 data-hide-mobile="0">
                ${this.buildColumnHeaderHtml(colId, widthDesktop)}
                ${this.buildColumnPlaceholderHtml()}
            </div>
        `;
    }

    generateColumns(count) {
        const preset = { id: 'custom', widths: Array(count).fill(Math.round(100 / count * 1000) / 1000) };
        return this.generateColumnsFromPreset(preset);
    }

    applyResponsiveVars(root = document) {
        root.querySelectorAll('.elementor-section').forEach(section => {
            this.applySectionStyles(section);
            this.applyColumnsGrid(section, 'desktop');
            this.applyColumnsGrid(section, 'tablet');
            this.applyColumnsGrid(section, 'mobile');
            this.applyColumnsGrid(section, this.deviceMode);
        });

        root.querySelectorAll('.elementor-column').forEach(col => {
            const d = col.dataset.widthDesktop || '100';
            const badge = col.querySelector('.elementor-column-badge');
            if (badge) {
                const deviceKey = this.getDeviceWidthKey(this.deviceMode);
                const current = col.dataset[deviceKey] || d;
                badge.textContent = `${current}%`;
            }
        });
    }

    getDeviceWidthKey(device) {
        if (device === 'tablet') return 'widthTablet';
        if (device === 'mobile') return 'widthMobile';
        return 'widthDesktop';
    }

    isColumnHidden(col, device) {
        if (device === 'tablet' && col.dataset.hideTablet === '1') return true;
        if (device === 'mobile' && col.dataset.hideMobile === '1') return true;
        return false;
    }

    buildGridTemplate(columns, device) {
        const visible = columns.filter(col => !this.isColumnHidden(col, device));
        if (visible.length === 0) return '1fr';

        const widthKey = this.getDeviceWidthKey(device);
        const widths = visible.map(col => parseFloat(col.dataset[widthKey] || col.dataset.widthDesktop || '100'));

        if (widths.length > 1 && widths.every(w => w >= 99.9)) {
            return '1fr';
        }

        return widths.map(w => `${w}fr`).join(' ');
    }

    applyColumnsGrid(section, device) {
        const columnsEl = section.querySelector('.elementor-columns');
        if (!columnsEl) return;

        const cols = Array.from(columnsEl.querySelectorAll('.elementor-column'));
        const template = this.buildGridTemplate(cols, device);
        const varName = device === 'tablet'
            ? '--grid-cols-tablet'
            : device === 'mobile'
                ? '--grid-cols-mobile'
                : '--grid-cols-desktop';

        columnsEl.style.setProperty(varName, template);
    }

    stripPublicGridInlineStyles(root = document) {
        root.querySelectorAll('.elementor-columns').forEach(el => {
            el.style.removeProperty('grid-template-columns');
        });
    }

    applySectionStyles(section) {
        const pt = section.dataset.paddingTop || '20';
        const pb = section.dataset.paddingBottom || '20';
        const gap = section.dataset.gap || '20';
        const bg = section.dataset.backgroundColor || '';
        section.style.setProperty('--section-padding-top', pt + 'px');
        section.style.setProperty('--section-padding-bottom', pb + 'px');
        section.style.setProperty('--section-gap', gap + 'px');
        section.style.paddingTop = pt + 'px';
        section.style.paddingBottom = pb + 'px';
        if (bg) section.style.backgroundColor = bg;
        const columns = section.querySelector('.elementor-columns');
        if (columns) columns.style.gap = gap + 'px';
        if (section.dataset.reverseMobile === '1') {
            section.dataset.reverseMobile = '1';
        }
    }

    applyDevicePreview() {
        const mode = this.deviceMode;
        document.querySelectorAll('.elementor-column').forEach(col => {
            col.classList.remove('elementor-col-hidden-preview');
            if (mode === 'tablet' && col.dataset.hideTablet === '1') col.classList.add('elementor-col-hidden-preview');
            if (mode === 'mobile' && col.dataset.hideMobile === '1') col.classList.add('elementor-col-hidden-preview');

            const badge = col.querySelector('.elementor-column-badge');
            if (badge) {
                const deviceKey = this.getDeviceWidthKey(mode);
                badge.textContent = `${col.dataset[deviceKey] || col.dataset.widthDesktop || '100'}%`;
            }
        });
        document.querySelectorAll('.elementor-section').forEach(section => {
            section.classList.toggle('elementor-section-reverse-preview', mode === 'mobile' && section.dataset.reverseMobile === '1');
            this.applyColumnsGrid(section, mode);
        });
    }

    ensureAddSectionButton() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (!canvas) return;
        if (!canvas.querySelector('.elementor-add-section-btn')) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'elementor-add-section-btn';
            btn.innerHTML = '<i class="fas fa-plus-circle"></i> افزودن بخش جدید';
            btn.addEventListener('click', () => this.openLayoutsPanel());
            canvas.appendChild(btn);
        }
    }

    hydrateEditorContent() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (!canvas) return;

        canvas.querySelectorAll('.elementor-section').forEach(section => {
            if (!section.dataset.id) section.dataset.id = this.generateId();
            if (!section.dataset.paddingTop) section.dataset.paddingTop = '20';
            if (!section.dataset.paddingBottom) section.dataset.paddingBottom = '20';
            if (!section.dataset.gap) section.dataset.gap = '20';
            if (!section.dataset.reverseMobile) section.dataset.reverseMobile = '0';
            if (!section.dataset.layout) section.dataset.layout = this.inferSectionLayout(section);

            if (!section.querySelector('.elementor-section-tools .elementor-drag-handle')) {
                section.querySelector('.elementor-section-tools')?.remove();
                section.insertAdjacentHTML('afterbegin', this.buildSectionToolsHtml(section.dataset.id));
            } else {
                this.updateElementToolIds(section.querySelector('.elementor-section-tools'), section.dataset.id);
                section.querySelectorAll('.elementor-section-tools [data-el-action], .elementor-section-tools [onclick]').forEach(btn => {
                    if (!btn.dataset.elAction) {
                        const onclick = btn.getAttribute('onclick') || '';
                        if (onclick.includes('editSection')) btn.dataset.elAction = 'edit-section';
                        else if (onclick.includes('duplicateSection')) btn.dataset.elAction = 'duplicate-section';
                        else if (onclick.includes('removeSection')) btn.dataset.elAction = 'remove-section';
                        btn.dataset.elId = section.dataset.id;
                        btn.removeAttribute('onclick');
                    }
                });
            }

            section.querySelectorAll('.elementor-column').forEach(col => {
                if (!col.dataset.id) col.dataset.id = this.generateId();
                if (!col.dataset.widthDesktop) col.dataset.widthDesktop = '100';
                if (!col.dataset.widthTablet) col.dataset.widthTablet = col.dataset.widthDesktop;
                if (!col.dataset.widthMobile) col.dataset.widthMobile = col.dataset.widthTablet;
                if (!col.dataset.hideTablet) col.dataset.hideTablet = '0';
                if (!col.dataset.hideMobile) col.dataset.hideMobile = '0';
                this.ensureColumnChrome(col);
                col.querySelectorAll('.elementor-column-header [onclick]').forEach(btn => {
                    btn.dataset.elAction = 'edit-column';
                    btn.dataset.elId = col.dataset.id;
                    btn.removeAttribute('onclick');
                });
            });

            section.querySelectorAll('.elementor-widget').forEach(widget => {
                if (!widget.dataset.id) widget.dataset.id = this.generateId();
                if (!widget.dataset.type) widget.dataset.type = 'text';
                widget.removeAttribute('draggable');

                if (!widget.querySelector('.elementor-widget-content')) {
                    const existingHtml = widget.innerHTML;
                    widget.innerHTML = `
                        ${this.buildWidgetToolsHtml(widget.dataset.id)}
                        <div class="elementor-widget-content">${existingHtml}</div>
                    `;
                } else if (!widget.querySelector('.elementor-widget-tools .elementor-drag-handle')) {
                    widget.querySelector('.elementor-widget-tools')?.remove();
                    widget.insertAdjacentHTML('afterbegin', this.buildWidgetToolsHtml(widget.dataset.id));
                } else {
                    this.updateElementToolIds(widget.querySelector('.elementor-widget-tools'), widget.dataset.id);
                    widget.querySelectorAll('.elementor-widget-tools [onclick]').forEach(btn => {
                        const onclick = btn.getAttribute('onclick') || '';
                        if (onclick.includes('editWidget')) btn.dataset.elAction = 'edit-widget';
                        else if (onclick.includes('duplicateWidget')) btn.dataset.elAction = 'duplicate-widget';
                        else if (onclick.includes('removeWidget')) btn.dataset.elAction = 'remove-widget';
                        btn.dataset.elId = widget.dataset.id;
                        btn.removeAttribute('onclick');
                    });
                }
            });
        });

        this.applyResponsiveVars(canvas);
        this.applyDevicePreview();
        this.ensureAddSectionButton();
        this.upgradeDragHandles();
        this.ensureDragHandlesDraggable(canvas);
        this.dndAudit('after-hydrate');
    }

    ensureDragHandlesDraggable(root = document) {
        root.querySelectorAll('.elementor-drag-handle[data-drag-kind]').forEach(handle => {
            handle.removeAttribute('draggable');
            handle.draggable = false;
        });

        document.querySelectorAll('.elementor-widget-item, .elementor-layout-preset').forEach(item => {
            item.setAttribute('draggable', 'true');
            item.draggable = true;
        });
    }

    sanitizeForSave(canvas) {
        const clone = canvas.cloneNode(true);
        this.normalizeInlineEditorsInClone(clone);
        clone.querySelectorAll('.elementor-section-tools, .elementor-widget-tools, .elementor-column-header, .elementor-column-tools, .elementor-column-placeholder, .elementor-add-section-btn, .elementor-empty-state').forEach(el => el.remove());
        clone.querySelectorAll('.elementor-section, .elementor-column, .elementor-widget').forEach(el => {
            el.classList.remove('active', 'drag-over', 'elementor-col-hidden-preview', 'elementor-section-reverse-preview', 'elementor-widget-dragging', 'elementor-section-dragging', 'elementor-column-dragging', 'is-inline-editing');
        });
        this.applyResponsiveVars(clone);
        this.stripPublicGridInlineStyles(clone);
        return clone.innerHTML.replace(/>\s+</g, '><').trim();
    }

    getDeviceLabel() {
        return { desktop: 'دسکتاپ', tablet: 'تبلت', mobile: 'موبایل' }[this.deviceMode] || 'دسکتاپ';
    }

    editSection(sectionId) {
        this.commitInlineTextEdit();
        const section = this.querySection(sectionId);
        if (!section) {
            this.syncSelection();
            return;
        }

        document.querySelectorAll('.elementor-section.active, .elementor-column.active, .elementor-widget.active').forEach(el => el.classList.remove('active'));
        section.classList.add('active');
        this.selectedElement = section;

        this.openSettingsView(this.getSectionSettings(sectionId), `بخش · ${this.getDeviceLabel()}`);
        this.setupSectionSettingsListeners(sectionId);
    }

    getSectionSettings(sectionId) {
        const section = this.querySection(sectionId);
        if (!section) return '';
        const pt = section.dataset.paddingTop || '20';
        const pb = section.dataset.paddingBottom || '20';
        const gap = section.dataset.gap || '20';
        const bg = section.dataset.backgroundColor || '#ffffff';
        const reverse = section.dataset.reverseMobile === '1';

        return `
            <p class="elementor-device-label"><i class="fas fa-desktop"></i> در حال ویرایش: ${this.getDeviceLabel()}</p>
            <h5 style="margin-bottom: 20px;">تنظیمات بخش</h5>
            <div class="elementor-control-group">
                <label class="elementor-control-label">قالب چیدمان</label>
                <select class="elementor-control-select" data-section-setting="layout">
                    ${ELEMENTOR_LAYOUT_PRESETS.map(p => `<option value="${p.id}" ${section.dataset.layout === p.id ? 'selected' : ''}>${p.name}</option>`).join('')}
                </select>
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">فاصله بالا (px)</label>
                <input type="number" class="elementor-control-input" data-section-setting="padding-top" value="${pt}" min="0" max="200">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">فاصله پایین (px)</label>
                <input type="number" class="elementor-control-input" data-section-setting="padding-bottom" value="${pb}" min="0" max="200">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">فاصله بین ستون‌ها (px)</label>
                <input type="number" class="elementor-control-input" data-section-setting="gap" value="${gap}" min="0" max="80">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">رنگ پس‌زمینه</label>
                <input type="color" class="elementor-control-color" data-section-setting="background-color" value="${bg}">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">
                    <input type="checkbox" data-section-setting="reverse-mobile" ${reverse ? 'checked' : ''}> معکوس کردن ستون‌ها در موبایل
                </label>
            </div>
        `;
    }

    setupSectionSettingsListeners(sectionId) {
        const panel = document.querySelector('.elementor-settings-content');
        panel.querySelectorAll('[data-section-setting]').forEach(input => {
            const handler = () => {
                if (!this.querySection(sectionId)) {
                    this.closeSettings();
                    return;
                }
                const setting = input.dataset.sectionSetting;
                let value = input.type === 'checkbox' ? (input.checked ? '1' : '0') : input.value;
                this.applySectionSetting(sectionId, setting, value);
                this.saveState();
            };
            input.addEventListener('change', handler);
            if (input.type === 'number' || input.type === 'color') input.addEventListener('input', handler);
        });
    }

    applySectionSetting(sectionId, setting, value) {
        const section = this.querySection(sectionId);
        if (!section) {
            this.syncSelection();
            return;
        }

        switch (setting) {
            case 'layout':
                section.dataset.layout = value;
                const preset = this.getPresetById(value);
                const columnsEl = section.querySelector('.elementor-columns');
                const existingWidgets = [];
                section.querySelectorAll('.elementor-column').forEach(col => {
                    existingWidgets.push(Array.from(col.querySelectorAll('.elementor-widget')).map(w => w.cloneNode(true)));
                });
                columnsEl.innerHTML = this.generateColumnsFromPreset(preset);
                section.querySelectorAll('.elementor-column').forEach((col, i) => {
                    if (existingWidgets[i]) {
                        existingWidgets[i].forEach(w => col.appendChild(w));
                        col.querySelector('.elementor-column-placeholder')?.remove();
                    }
                });
                this.applyResponsiveVars(section);
                break;
            case 'padding-top':
                section.dataset.paddingTop = value;
                break;
            case 'padding-bottom':
                section.dataset.paddingBottom = value;
                break;
            case 'gap':
                section.dataset.gap = value;
                break;
            case 'background-color':
                section.dataset.backgroundColor = value;
                break;
            case 'reverse-mobile':
                section.dataset.reverseMobile = value;
                break;
        }
        this.applySectionStyles(section);
        this.applyDevicePreview();
    }

    editColumn(colId) {
        this.commitInlineTextEdit();
        const col = this.queryColumn(colId);
        if (!col) {
            this.syncSelection();
            return;
        }

        document.querySelectorAll('.elementor-section.active, .elementor-column.active, .elementor-widget.active').forEach(el => el.classList.remove('active'));
        col.classList.add('active');
        this.selectedElement = col;

        this.openSettingsView(this.getColumnSettings(colId), `ستون · ${this.getDeviceLabel()}`);
        this.setupColumnSettingsListeners(colId);
    }

    getColumnSettings(colId) {
        const col = this.queryColumn(colId);
        if (!col) return '';
        const device = this.deviceMode;
        const widthKey = `width${device.charAt(0).toUpperCase()}${device.slice(1)}`;
        const currentWidth = col.dataset[`width${device.charAt(0).toUpperCase()}${device.slice(1)}`] || col.dataset.widthDesktop || '100';

        return `
            <p class="elementor-device-label"><i class="fas fa-desktop"></i> در حال ویرایش: ${this.getDeviceLabel()}</p>
            <h5 style="margin-bottom: 20px;">تنظیمات ستون</h5>
            <div class="elementor-control-group">
                <label class="elementor-control-label">عرض (${this.getDeviceLabel()})</label>
                <select class="elementor-control-select" data-column-setting="width" data-device="${device}">
                    ${[25, 33.333, 50, 66.666, 75, 100].map(w => `<option value="${w}" ${parseFloat(currentWidth) === w ? 'selected' : ''}>${w}%</option>`).join('')}
                </select>
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">
                    <input type="checkbox" data-column-setting="hide-tablet" ${col.dataset.hideTablet === '1' ? 'checked' : ''}> مخفی در تبلت
                </label>
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">
                    <input type="checkbox" data-column-setting="hide-mobile" ${col.dataset.hideMobile === '1' ? 'checked' : ''}> مخفی در موبایل
                </label>
            </div>
        `;
    }

    setupColumnSettingsListeners(colId) {
        const panel = document.querySelector('.elementor-settings-content');
        panel.querySelectorAll('[data-column-setting]').forEach(input => {
            input.addEventListener('change', () => {
                if (!this.queryColumn(colId)) {
                    this.closeSettings();
                    return;
                }
                const setting = input.dataset.columnSetting;
                let value = input.type === 'checkbox' ? (input.checked ? '1' : '0') : input.value;
                this.applyColumnSetting(colId, setting, value, input.dataset.device || this.deviceMode);
                this.saveState();
            });
        });
    }

    applyColumnSetting(colId, setting, value, device) {
        const col = this.queryColumn(colId);
        if (!col) {
            this.syncSelection();
            return;
        }

        switch (setting) {
            case 'width':
                col.dataset[`width${device.charAt(0).toUpperCase()}${device.slice(1)}`] = value;
                break;
            case 'hide-tablet':
                col.dataset.hideTablet = value;
                break;
            case 'hide-mobile':
                col.dataset.hideMobile = value;
                break;
        }
        this.applyResponsiveVars(col.closest('.elementor-section') || document);
        this.applyDevicePreview();
    }
    
    addWidget(type, column, openSettings = true, dropTarget = null) {
        const widgetId = this.generateId();
        const widget = document.createElement('div');
        widget.className = 'elementor-widget';
        widget.dataset.id = widgetId;
        widget.dataset.type = type;

        widget.innerHTML = `
            ${this.buildWidgetToolsHtml(widgetId)}
            <div class="elementor-widget-content">
                ${this.getWidgetDefaultContent(type)}
            </div>
        `;

        if (dropTarget) {
            this.insertWidgetAt(widget, dropTarget);
        } else {
            column.querySelector('.elementor-column-placeholder')?.remove();
            column.appendChild(widget);
            this.ensureColumnPlaceholder(column);
        }

        this.saveState();
        if (openSettings) this.editWidget(widgetId);
    }
    
    getWidgetDefaultContent(type) {
        const defaults = {
            heading: '<div class="widget-heading"><h2>عنوان جدید</h2></div>',
            text: '<div class="widget-text"><p>این یک متن نمونه است. محتوای خود را اینجا بنویسید.</p></div>',
            image: '<div class="widget-image"><img src="/images/seed/placeholder.jpg" alt="تصویر"></div>',
            video: '<div class="widget-video"><iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ" allowfullscreen allow="autoplay; fullscreen; encrypted-media; picture-in-picture"></iframe></div>',
            audio: `
                <div class="widget-audio">
                    <div class="widget-audio-meta">
                        <strong class="widget-audio-title">عنوان صدا</strong>
                        <span class="widget-audio-desc">پادکست، موسیقی یا فایل صوتی</span>
                    </div>
                    <audio class="widget-audio-player" controls preload="metadata" src="">
                        مرورگر شما از پخش صوت پشتیبانی نمی‌کند.
                    </audio>
                </div>`,
            button: '<div class="widget-button"><a href="#" class="btn-widget">کلیک کنید</a></div>',
            divider: '<div class="widget-divider"><hr></div>',
            spacer: '<div class="widget-spacer"></div>',
            icon: '<div class="widget-icon"><i class="fas fa-star"></i></div>',
            list: '<div class="widget-list"><ul><li>مورد اول</li><li>مورد دوم</li><li>مورد سوم</li></ul></div>',
            quote: '<div class="widget-quote"><blockquote><p>این یک نقل قول است</p></blockquote></div>',
            html: '<div class="widget-html"><p>محتوای HTML سفارشی</p></div>',
            alert: '<div class="widget-alert alert alert-info"><p>این یک پیام هشدار است</p></div>'
        };
        
        return defaults[type] || '<p>ویجت</p>';
    }
    
    editWidget(widgetId) {
        const widget = this.queryWidget(widgetId);
        if (!widget) {
            this.syncSelection();
            return;
        }

        const type = widget.dataset.type;
        const alreadyEditing = ELEMENTOR_INLINE_TYPES.has(type)
            && this._textEditorWidgetId === widgetId
            && this.textEditor;

        if (!alreadyEditing) {
            this.commitInlineTextEdit();
        }

        document.querySelectorAll('.elementor-section.active, .elementor-column.active, .elementor-widget.active').forEach(el => el.classList.remove('active'));
        widget.classList.add('active');

        this.selectedElement = widget;

        this.openSettingsView(this.getWidgetSettings(type, widgetId), `${this.getWidgetName(type)} · ${this.getDeviceLabel()}`);
        this.setupSettingsListeners(widgetId);

        if (ELEMENTOR_INLINE_TYPES.has(type) && !alreadyEditing) {
            this.startInlineTextEdit(widgetId);
        }
    }
    
    getWidgetSettings(type, widgetId) {
        const widget = this.queryWidget(widgetId);
        if (!widget) return '';
        const content = widget.querySelector('.elementor-widget-content');
        
        let html = `<h5 style="margin-bottom: 20px;">تنظیمات ${this.getWidgetName(type)}</h5>`;
        
        switch(type) {
            case 'heading': {
                const heading = content.querySelector('h1, h2, h3, h4, h5, h6');
                const currentTag = heading ? heading.tagName.toLowerCase() : 'h2';
                const headingColor = heading?.style.color || '#54595f';
                const headingAlign = content.querySelector('.widget-heading')?.style.textAlign || 'right';

                html += `
                    <div class="elementor-control-group">
                        <div class="elementor-inline-edit-hint">
                            <i class="fas fa-i-cursor"></i>
                            <div>
                                <strong>ویرایش روی بوم</strong>
                                <span>عنوان را مستقیم روی بوم ویرایش کنید.</span>
                            </div>
                        </div>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">نوع عنوان</label>
                        <select class="elementor-control-select" data-setting="heading-tag">
                            <option value="h1" ${currentTag === 'h1' ? 'selected' : ''}>H1</option>
                            <option value="h2" ${currentTag === 'h2' ? 'selected' : ''}>H2</option>
                            <option value="h3" ${currentTag === 'h3' ? 'selected' : ''}>H3</option>
                            <option value="h4" ${currentTag === 'h4' ? 'selected' : ''}>H4</option>
                            <option value="h5" ${currentTag === 'h5' ? 'selected' : ''}>H5</option>
                            <option value="h6" ${currentTag === 'h6' ? 'selected' : ''}>H6</option>
                        </select>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">رنگ متن</label>
                        <input type="color" class="elementor-control-color" data-setting="heading-color" value="${this.escapeAttr(headingColor.startsWith('#') ? headingColor : '#54595f')}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">تراز متن</label>
                        <div class="elementor-control-buttons">
                            <button type="button" class="elementor-control-btn${headingAlign === 'right' ? ' active' : ''}" data-setting="heading-align" data-value="right">
                                <i class="fas fa-align-right"></i>
                            </button>
                            <button type="button" class="elementor-control-btn${headingAlign === 'center' ? ' active' : ''}" data-setting="heading-align" data-value="center">
                                <i class="fas fa-align-center"></i>
                            </button>
                            <button type="button" class="elementor-control-btn${headingAlign === 'left' ? ' active' : ''}" data-setting="heading-align" data-value="left">
                                <i class="fas fa-align-left"></i>
                            </button>
                        </div>
                    </div>
                `;
                break;
            }
                
            case 'text': {
                html += `
                    <div class="elementor-control-group">
                        <div class="elementor-inline-edit-hint">
                            <i class="fas fa-i-cursor"></i>
                            <div>
                                <strong>ویرایش روی بوم</strong>
                                <span>اندازه فونت، رنگ، پس‌زمینه، تورفتگی و بقیه استایل‌ها از تولبار بالای متن در دسترس است.</span>
                            </div>
                        </div>
                    </div>
                `;
                break;
            }

            case 'quote': {
                html += `
                    <div class="elementor-control-group">
                        <div class="elementor-inline-edit-hint">
                            <i class="fas fa-quote-right"></i>
                            <div>
                                <strong>ویرایش روی بوم</strong>
                                <span>متن نقل قول را مستقیم روی بوم بنویسید و قالب‌بندی کنید.</span>
                            </div>
                        </div>
                    </div>
                `;
                break;
            }

            case 'alert': {
                const alertEl = content.querySelector('.widget-alert');
                const alertType = ['info', 'success', 'warning', 'danger']
                    .find(t => alertEl?.classList.contains(`alert-${t}`)) || 'info';

                html += `
                    <div class="elementor-control-group">
                        <div class="elementor-inline-edit-hint">
                            <i class="fas fa-exclamation-triangle"></i>
                            <div>
                                <strong>ویرایش روی بوم</strong>
                                <span>متن هشدار را مستقیم روی بوم ویرایش کنید.</span>
                            </div>
                        </div>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">نوع هشدار</label>
                        <select class="elementor-control-select" data-setting="alert-type">
                            <option value="info" ${alertType === 'info' ? 'selected' : ''}>اطلاعات</option>
                            <option value="success" ${alertType === 'success' ? 'selected' : ''}>موفقیت</option>
                            <option value="warning" ${alertType === 'warning' ? 'selected' : ''}>هشدار</option>
                            <option value="danger" ${alertType === 'danger' ? 'selected' : ''}>خطر</option>
                        </select>
                    </div>
                `;
                break;
            }

            case 'list': {
                const listEl = content.querySelector('.widget-list ul, .widget-list ol');
                const listTag = listEl?.tagName?.toLowerCase() === 'ol' ? 'ol' : 'ul';
                const listItems = listEl
                    ? Array.from(listEl.querySelectorAll(':scope > li'))
                    : [];

                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">نوع لیست</label>
                        <select class="elementor-control-select" data-setting="list-style">
                            <option value="ul" ${listTag === 'ul' ? 'selected' : ''}>نقطه‌ای (Bullet)</option>
                            <option value="ol" ${listTag === 'ol' ? 'selected' : ''}>شماره‌دار</option>
                        </select>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">موارد لیست</label>
                        <div class="elementor-list-editor">
                            ${listItems.map((li, index) => `
                                <div class="elementor-list-item-row">
                                    <span class="elementor-list-item-num">${index + 1}</span>
                                    <input type="text" class="elementor-control-input elementor-list-item-input"
                                           data-index="${index}" value="${this.escapeAttr(li.textContent || '')}"
                                           placeholder="متن مورد ${index + 1}">
                                    <button type="button" class="elementor-list-item-remove" data-action="list-remove" data-index="${index}" title="حذف مورد">
                                        <i class="fas fa-trash-alt"></i>
                                    </button>
                                </div>
                            `).join('') || `
                                <div class="elementor-list-item-row">
                                    <span class="elementor-list-item-num">1</span>
                                    <input type="text" class="elementor-control-input elementor-list-item-input"
                                           data-index="0" value="مورد اول" placeholder="متن مورد">
                                    <button type="button" class="elementor-list-item-remove" data-action="list-remove" data-index="0" title="حذف مورد">
                                        <i class="fas fa-trash-alt"></i>
                                    </button>
                                </div>
                            `}
                        </div>
                        <button type="button" class="elementor-list-add-btn" data-action="list-add">
                            <i class="fas fa-plus"></i>
                            افزودن مورد
                        </button>
                    </div>
                `;
                break;
            }
                
            case 'image':
                const img = content.querySelector('img');
                const currentSrc = img ? img.src : '';
                const currentAlt = img ? img.alt : '';
                
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">آپلود تصویر</label>
                        <div class="elementor-upload-zone" onclick="document.getElementById('image-upload-${widgetId}').click()">
                            <i class="fas fa-cloud-upload-alt"></i>
                            <p>کلیک کنید یا تصویر را اینجا بکشید</p>
                            <small>JPG, PNG, GIF, WebP (حداکثر 5MB)</small>
                        </div>
                        <input type="file" id="image-upload-${widgetId}" accept="image/*" style="display:none;" data-widget-id="${widgetId}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">یا آدرس URL تصویر</label>
                        <input type="text" class="elementor-control-input" data-setting="image-src" value="${currentSrc}" placeholder="https://example.com/image.jpg">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">متن جایگزین (Alt)</label>
                        <input type="text" class="elementor-control-input" data-setting="image-alt" value="${currentAlt}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">تراز تصویر</label>
                        <div class="elementor-control-buttons">
                            <button type="button" class="elementor-control-btn" data-setting="image-align" data-value="right">
                                <i class="fas fa-align-right"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="image-align" data-value="center">
                                <i class="fas fa-align-center"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="image-align" data-value="left">
                                <i class="fas fa-align-left"></i>
                            </button>
                        </div>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">عرض (%)</label>
                        <input type="number" class="elementor-control-input" data-setting="image-width" value="100" min="10" max="100">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">شعاع گوشه (px)</label>
                        <input type="number" class="elementor-control-input" data-setting="image-radius" value="4" min="0" max="100">
                    </div>
                `;
                break;
                
            case 'button':
                const btn = content.querySelector('a');
                const btnText = btn ? btn.textContent : 'کلیک کنید';
                const btnHref = btn ? btn.href : '#';
                
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">متن دکمه</label>
                        <input type="text" class="elementor-control-input" data-setting="button-text" value="${btnText}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">لینک</label>
                        <input type="text" class="elementor-control-input" data-setting="button-link" value="${btnHref}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">رنگ پس‌زمینه</label>
                        <input type="color" class="elementor-control-color" data-setting="button-bg" value="#93003c">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">رنگ متن</label>
                        <input type="color" class="elementor-control-color" data-setting="button-color" value="#ffffff">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">تراز</label>
                        <div class="elementor-control-buttons">
                            <button type="button" class="elementor-control-btn" data-setting="button-align" data-value="right">
                                <i class="fas fa-align-right"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="button-align" data-value="center">
                                <i class="fas fa-align-center"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="button-align" data-value="left">
                                <i class="fas fa-align-left"></i>
                            </button>
                        </div>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">شعاع گوشه (px)</label>
                        <input type="number" class="elementor-control-input" data-setting="button-radius" value="4" min="0" max="50">
                    </div>
                `;
                break;
                
            case 'divider':
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">رنگ خط</label>
                        <input type="color" class="elementor-control-color" data-setting="divider-color" value="#d5d8dc">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">ضخامت (px)</label>
                        <input type="number" class="elementor-control-input" data-setting="divider-weight" value="2" min="1" max="10">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">عرض (%)</label>
                        <input type="number" class="elementor-control-input" data-setting="divider-width" value="100" min="10" max="100">
                    </div>
                `;
                break;
                
            case 'spacer':
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">ارتفاع (px)</label>
                        <input type="number" class="elementor-control-input" data-setting="spacer-height" value="50" min="10" max="500">
                    </div>
                `;
                break;
                
            case 'icon':
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">کلاس آیکون (Font Awesome)</label>
                        <input type="text" class="elementor-control-input" data-setting="icon-class" value="fas fa-star" placeholder="fas fa-star">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">اندازه (px)</label>
                        <input type="number" class="elementor-control-input" data-setting="icon-size" value="64" min="16" max="200">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">رنگ</label>
                        <input type="color" class="elementor-control-color" data-setting="icon-color" value="#93003c">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">تراز</label>
                        <div class="elementor-control-buttons">
                            <button type="button" class="elementor-control-btn" data-setting="icon-align" data-value="right">
                                <i class="fas fa-align-right"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="icon-align" data-value="center">
                                <i class="fas fa-align-center"></i>
                            </button>
                            <button type="button" class="elementor-control-btn" data-setting="icon-align" data-value="left">
                                <i class="fas fa-align-left"></i>
                            </button>
                        </div>
                    </div>
                `;
                break;
                
            case 'video':
                const iframe = content.querySelector('iframe');
                const videoSrc = iframe ? iframe.src : '';
                
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">آدرس ویدیو</label>
                        <input type="text" class="elementor-control-input" data-setting="video-src" value="${this.escapeHtml(videoSrc)}" placeholder="YouTube، آپارات، Vimeo یا لینک embed">
                        <small class="elementor-control-hint">مثال آپارات: https://www.aparat.com/v/xxxxx</small>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">نسبت تصویر</label>
                        <select class="elementor-control-select" data-setting="video-ratio">
                            <option value="16-9">16:9</option>
                            <option value="4-3">4:3</option>
                            <option value="1-1">1:1</option>
                        </select>
                    </div>
                `;
                break;

            case 'audio': {
                const audioEl = content.querySelector('audio');
                const audioTitle = content.querySelector('.widget-audio-title');
                const audioDesc = content.querySelector('.widget-audio-desc');
                const audioSrc = audioEl ? (audioEl.getAttribute('src') || audioEl.querySelector('source')?.getAttribute('src') || '') : '';
                const autoplay = audioEl?.hasAttribute('autoplay') ? 'true' : 'false';
                const loop = audioEl?.hasAttribute('loop') ? 'true' : 'false';

                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">آپلود فایل صوتی</label>
                        <div class="elementor-upload-zone" onclick="document.getElementById('audio-upload-${widgetId}').click()">
                            <i class="fas fa-file-audio"></i>
                            <p>کلیک کنید یا فایل صوتی را اینجا بکشید</p>
                            <small>MP3, WAV, OGG, M4A (حداکثر ${this.config.maxUploadSizeMb}MB)</small>
                        </div>
                        <input type="file" id="audio-upload-${widgetId}" accept="audio/*,.mp3,.wav,.ogg,.m4a,.aac,.webm" style="display:none;" data-widget-id="${widgetId}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">یا آدرس URL فایل صوتی</label>
                        <input type="text" class="elementor-control-input" data-setting="audio-src" value="${audioSrc}" placeholder="https://example.com/podcast.mp3">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">عنوان</label>
                        <input type="text" class="elementor-control-input" data-setting="audio-title" value="${this.escapeHtml(audioTitle?.textContent || '')}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">توضیح کوتاه</label>
                        <input type="text" class="elementor-control-input" data-setting="audio-desc" value="${this.escapeHtml(audioDesc?.textContent || '')}">
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">پخش خودکار</label>
                        <select class="elementor-control-select" data-setting="audio-autoplay">
                            <option value="false" ${autoplay === 'false' ? 'selected' : ''}>خیر</option>
                            <option value="true" ${autoplay === 'true' ? 'selected' : ''}>بله</option>
                        </select>
                    </div>
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">تکرار (Loop)</label>
                        <select class="elementor-control-select" data-setting="audio-loop">
                            <option value="false" ${loop === 'false' ? 'selected' : ''}>خیر</option>
                            <option value="true" ${loop === 'true' ? 'selected' : ''}>بله</option>
                        </select>
                    </div>
                `;
                break;
            }
                
            case 'html':
                const htmlContent = content.innerHTML;
                
                html += `
                    <div class="elementor-control-group">
                        <label class="elementor-control-label">کد HTML</label>
                        <textarea class="elementor-control-input" data-setting="html-content" rows="10" style="font-family: monospace;">${this.escapeHtml(htmlContent)}</textarea>
                    </div>
                `;
                break;
        }
        
        // Common spacing settings
        html += `
            <hr style="margin: 30px 0; border-color: var(--elementor-border);">
            <h6 style="margin-bottom: 15px; color: var(--elementor-secondary);">فاصله‌گذاری</h6>
            <div class="elementor-control-group">
                <label class="elementor-control-label">فاصله بالا (px)</label>
                <input type="number" class="elementor-control-input" data-setting="margin-top" value="0" min="0" max="200">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">فاصله پایین (px)</label>
                <input type="number" class="elementor-control-input" data-setting="margin-bottom" value="0" min="0" max="200">
            </div>
            <div class="elementor-control-group">
                <label class="elementor-control-label">پدینگ (px)</label>
                <input type="number" class="elementor-control-input" data-setting="padding" value="0" min="0" max="100">
            </div>
        `;
        
        return html;
    }
    
    setupSettingsListeners(widgetId) {
        const widget = this.queryWidget(widgetId);
        if (!widget) return;

        const settingsPanel = document.querySelector('.elementor-settings-content');
        
        // Input and select changes
        settingsPanel.querySelectorAll('input, select, textarea').forEach(input => {
            if (input.classList.contains('elementor-list-item-input')) return;
            if (input.closest('.ql-toolbar') || input.closest('.ql-container')) return;

            input.addEventListener('input', () => {
                if (!this.queryWidget(widgetId)) {
                    this.closeSettings();
                    return;
                }
                this.applyWidgetSetting(widgetId, input.dataset.setting, input.value);
            });

            input.addEventListener('change', () => {
                if (!this.queryWidget(widgetId)) {
                    this.closeSettings();
                    return;
                }
                this.applyWidgetSetting(widgetId, input.dataset.setting, input.value);
                this.saveState();
            });
        });
        
        // Button groups
        settingsPanel.querySelectorAll('.elementor-control-btn[data-setting]').forEach(btn => {
            btn.addEventListener('click', () => {
                const setting = btn.dataset.setting;
                const value = btn.dataset.value;
                
                // Remove active from siblings
                btn.parentElement.querySelectorAll('.elementor-control-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                
                this.applyWidgetSetting(widgetId, setting, value);
                this.saveState();
            });
        });

        this.setupListSettingsListeners(widgetId);
        
        // Image upload handler
        const imageUpload = settingsPanel.querySelector(`#image-upload-${widgetId}`);
        if (imageUpload) {
            imageUpload.addEventListener('change', (e) => {
                this.handleImageUpload(e.target.files[0], widgetId);
            });
            
            // Drag and drop support
            const uploadZone = settingsPanel.querySelector('.elementor-upload-zone');
            if (uploadZone) {
                uploadZone.addEventListener('dragover', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    uploadZone.style.borderColor = 'var(--elementor-primary)';
                    uploadZone.style.background = 'rgba(147, 0, 60, 0.05)';
                });
                
                uploadZone.addEventListener('dragleave', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    uploadZone.style.borderColor = '';
                    uploadZone.style.background = '';
                });
                
                uploadZone.addEventListener('drop', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    uploadZone.style.borderColor = '';
                    uploadZone.style.background = '';
                    
                    const files = e.dataTransfer.files;
                    if (files.length > 0) {
                        this.handleImageUpload(files[0], widgetId);
                    }
                });
            }
        }

        const audioUpload = settingsPanel.querySelector(`#audio-upload-${widgetId}`);
        if (audioUpload) {
            audioUpload.addEventListener('change', (e) => {
                this.handleAudioUpload(e.target.files[0], widgetId);
            });

            const audioUploadZone = audioUpload.previousElementSibling;
            if (audioUploadZone?.classList.contains('elementor-upload-zone')) {
                audioUploadZone.addEventListener('dragover', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    audioUploadZone.style.borderColor = 'var(--elementor-primary)';
                    audioUploadZone.style.background = 'rgba(147, 0, 60, 0.05)';
                });

                audioUploadZone.addEventListener('dragleave', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    audioUploadZone.style.borderColor = '';
                    audioUploadZone.style.background = '';
                });

                audioUploadZone.addEventListener('drop', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    audioUploadZone.style.borderColor = '';
                    audioUploadZone.style.background = '';

                    const files = e.dataTransfer.files;
                    if (files.length > 0) {
                        this.handleAudioUpload(files[0], widgetId);
                    }
                });
            }
        }
    }

    resolveVideoEmbedUrl(input) {
        if (!input || typeof input !== 'string') return '';
        const url = input.trim();
        if (!url) return '';

        if (/youtube\.com\/embed\//i.test(url) || /youtube-nocookie\.com\/embed\//i.test(url)) {
            return url;
        }
        if (/aparat\.com\/video\/video\/embed\//i.test(url)) {
            return url;
        }
        if (/player\.vimeo\.com\/video\//i.test(url)) {
            return url;
        }

        let match = url.match(/(?:youtube\.com\/(?:watch\?.*v=|embed\/|shorts\/)|youtu\.be\/)([\w-]{11})/i);
        if (match) {
            return `https://www.youtube.com/embed/${match[1]}`;
        }

        match = url.match(/aparat\.com\/v\/([^/?#\s]+)/i);
        if (match) {
            return `https://www.aparat.com/video/video/embed/videohash/${match[1]}/vt/frame`;
        }

        match = url.match(/aparat\.com\/video\/video\/(?:embed\/)?videohash\/([^/?#\s]+)/i);
        if (match) {
            return `https://www.aparat.com/video/video/embed/videohash/${match[1]}/vt/frame`;
        }

        match = url.match(/vimeo\.com\/(?:channels\/[^/]+\/|groups\/[^/]+\/videos\/|video\/)?(\d+)/i);
        if (match) {
            return `https://player.vimeo.com/video/${match[1]}`;
        }

        return url;
    }

    applyVideoEmbed(content, rawUrl) {
        const embedUrl = this.resolveVideoEmbedUrl(rawUrl);
        const videoIframe = content.querySelector('iframe');
        if (videoIframe) {
            videoIframe.src = embedUrl;
            videoIframe.setAttribute('allowfullscreen', '');
            videoIframe.setAttribute('allow', 'autoplay; fullscreen; encrypted-media; picture-in-picture');
        }
        return embedUrl;
    }
    
    async handleImageUpload(file, widgetId) {
        if (!file) return;
        
        // Validate file type
        const allowedTypes = ['image/jpeg', 'image/jpg', 'image/png', 'image/gif', 'image/webp'];
        if (!allowedTypes.includes(file.type)) {
            this.showMessage('فرمت فایل مجاز نیست. فقط JPG, PNG, GIF و WebP', 'error');
            return;
        }
        
        if (file.size > this.getMaxUploadBytes()) {
            this.showMessage(`حجم فایل نباید بیشتر از ${this.config.maxUploadSizeMb} مگابایت باشد`, 'error');
            return;
        }
        
        // Show loading
        const loadingDiv = document.createElement('div');
        loadingDiv.className = 'elementor-loading';
        loadingDiv.innerHTML = '<div class="elementor-spinner"></div>';
        document.body.appendChild(loadingDiv);
        
        try {
            // Create form data
            const formData = new FormData();
            formData.append('file', file);
            
            // Upload to server
            const response = await fetch(this.config.uploadUrl, {
                method: 'POST',
                body: formData
            });
            
            const result = await response.json();
            
            if (result.success) {
                // Update image src
                this.applyWidgetSetting(widgetId, 'image-src', result.url);
                
                // Update the input field
                const urlInput = document.querySelector('.elementor-settings-content input[data-setting="image-src"]');
                if (urlInput) {
                    urlInput.value = result.url;
                }
                
                this.saveState();
                this.showMessage('تصویر با موفقیت آپلود شد', 'success');
            } else {
                this.showMessage(result.message || 'خطا در آپلود تصویر', 'error');
            }
        } catch (error) {
            console.error('Upload error:', error);
            this.showMessage('خطا در آپلود تصویر', 'error');
        } finally {
            // Remove loading
            loadingDiv.remove();
        }
    }

    async handleAudioUpload(file, widgetId) {
        if (!file) return;

        const allowedTypes = [
            'audio/mpeg', 'audio/mp3', 'audio/wav', 'audio/x-wav', 'audio/ogg',
            'audio/mp4', 'audio/aac', 'audio/webm', 'audio/x-m4a'
        ];
        const allowedExt = ['.mp3', '.mpeg', '.wav', '.ogg', '.m4a', '.aac', '.webm'];
        const ext = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';

        if (!allowedTypes.includes(file.type) && !allowedExt.includes(ext)) {
            this.showMessage('فرمت فایل صوتی مجاز نیست. MP3, WAV, OGG, M4A', 'error');
            return;
        }

        if (file.size > this.getMaxUploadBytes()) {
            this.showMessage(`حجم فایل نباید بیشتر از ${this.config.maxUploadSizeMb} مگابایت باشد`, 'error');
            return;
        }

        const loadingDiv = document.createElement('div');
        loadingDiv.className = 'elementor-loading';
        loadingDiv.innerHTML = '<div class="elementor-spinner"></div>';
        document.body.appendChild(loadingDiv);

        try {
            const formData = new FormData();
            formData.append('file', file);

            const response = await fetch(this.config.uploadUrl, {
                method: 'POST',
                body: formData
            });

            const result = await response.json();

            if (result.success) {
                this.applyWidgetSetting(widgetId, 'audio-src', result.url);

                const urlInput = document.querySelector('.elementor-settings-content input[data-setting="audio-src"]');
                if (urlInput) urlInput.value = result.url;

                const widget = this.queryWidget(widgetId);
                const player = widget?.querySelector('.widget-audio-player');
                if (player) player.load();

                this.saveState();
                this.showMessage('فایل صوتی با موفقیت آپلود شد', 'success');
            } else {
                this.showMessage(result.message || 'خطا در آپلود فایل صوتی', 'error');
            }
        } catch (error) {
            console.error('Audio upload error:', error);
            this.showMessage('خطا در آپلود فайل صوتی', 'error');
        } finally {
            loadingDiv.remove();
        }
    }
    
    applyWidgetSetting(widgetId, setting, value) {
        const widget = this.queryWidget(widgetId);
        if (!widget) {
            this.syncSelection();
            return;
        }
        
        const content = widget.querySelector('.elementor-widget-content');
        const type = widget.dataset.type;
        
        switch(setting) {
            case 'heading-text':
                const heading = content.querySelector('h1, h2, h3, h4, h5, h6');
                if (heading) heading.textContent = value;
                break;
                
            case 'heading-tag':
                const oldHeading = content.querySelector('h1, h2, h3, h4, h5, h6');
                if (oldHeading) {
                    const newHeading = document.createElement(value);
                    newHeading.innerHTML = oldHeading.innerHTML;
                    newHeading.style.cssText = oldHeading.style.cssText;
                    if (oldHeading.isContentEditable) {
                        newHeading.contentEditable = 'true';
                        newHeading.classList.add('elementor-inline-heading');
                        oldHeading.replaceWith(newHeading);
                        this.textEditor = {
                            root: newHeading,
                            focus: () => newHeading.focus(),
                            isHeading: true
                        };
                    } else {
                        oldHeading.replaceWith(newHeading);
                    }
                }
                break;
                
            case 'heading-color':
                const h = content.querySelector('h1, h2, h3, h4, h5, h6');
                if (h) h.style.color = value;
                break;
                
            case 'heading-align':
                const hAlign = content.querySelector('.widget-heading');
                if (hAlign) hAlign.style.textAlign = value;
                break;

            case 'alert-type': {
                const alertTarget = content.querySelector('.widget-alert')
                    || content.querySelector('.elementor-inline-text-editor');
                if (!alertTarget) break;
                if (alertTarget.classList.contains('elementor-inline-text-editor')) {
                    alertTarget.dataset.restoreClass = `widget-alert alert alert-${value}`;
                } else {
                    alertTarget.className = `widget-alert alert alert-${value}`;
                }
                break;
            }
                
            case 'text-content': {
                const textRoot = content.querySelector('.widget-text');
                if (textRoot) textRoot.innerHTML = value || '<p><br></p>';
                break;
            }
                
            case 'text-color': {
                this.applyInlineTextStyle(widgetId, 'color', value);
                break;
            }
                
            case 'text-size': {
                this.applyInlineTextStyle(widgetId, 'fontSize', value + 'px');
                break;
            }
                
            case 'text-align': {
                this.applyInlineTextStyle(widgetId, 'textAlign', value);
                break;
            }

            case 'list-style': {
                const oldList = content.querySelector('.widget-list ul, .widget-list ol');
                const wrap = content.querySelector('.widget-list');
                if (!wrap) break;
                const nextTag = value === 'ol' ? 'ol' : 'ul';
                if (oldList && oldList.tagName.toLowerCase() === nextTag) break;
                const neu = document.createElement(nextTag);
                if (oldList) {
                    neu.innerHTML = oldList.innerHTML;
                    oldList.replaceWith(neu);
                } else {
                    neu.innerHTML = '<li>مورد اول</li>';
                    wrap.appendChild(neu);
                }
                break;
            }
                
            case 'image-src':
                const img = content.querySelector('img');
                if (img) img.src = value;
                break;
                
            case 'image-alt':
                const imgAlt = content.querySelector('img');
                if (imgAlt) imgAlt.alt = value;
                break;
                
            case 'image-align':
                const imgDiv = content.querySelector('.widget-image');
                if (imgDiv) imgDiv.style.textAlign = value;
                break;
                
            case 'image-width':
                const imgWidth = content.querySelector('img');
                if (imgWidth) imgWidth.style.width = value + '%';
                break;
                
            case 'image-radius':
                const imgRadius = content.querySelector('img');
                if (imgRadius) imgRadius.style.borderRadius = value + 'px';
                break;
                
            case 'button-text':
                const btnText = content.querySelector('a');
                if (btnText) btnText.textContent = value;
                break;
                
            case 'button-link':
                const btnLink = content.querySelector('a');
                if (btnLink) btnLink.href = value;
                break;
                
            case 'button-bg':
                const btnBg = content.querySelector('a');
                if (btnBg) btnBg.style.backgroundColor = value;
                break;
                
            case 'button-color':
                const btnColor = content.querySelector('a');
                if (btnColor) btnColor.style.color = value;
                break;
                
            case 'button-align':
                const btnAlign = content.querySelector('.widget-button');
                if (btnAlign) btnAlign.style.textAlign = value;
                break;
                
            case 'button-radius':
                const btnRadius = content.querySelector('a');
                if (btnRadius) btnRadius.style.borderRadius = value + 'px';
                break;
                
            case 'divider-color':
                const hr = content.querySelector('hr');
                if (hr) hr.style.borderTopColor = value;
                break;
                
            case 'divider-weight':
                const hrWeight = content.querySelector('hr');
                if (hrWeight) hrWeight.style.borderTopWidth = value + 'px';
                break;
                
            case 'divider-width':
                const hrWidth = content.querySelector('hr');
                if (hrWidth) hrWidth.style.width = value + '%';
                break;
                
            case 'spacer-height':
                const spacer = content.querySelector('.widget-spacer');
                if (spacer) spacer.style.height = value + 'px';
                break;
                
            case 'icon-class':
                const icon = content.querySelector('i');
                if (icon) {
                    icon.className = value;
                }
                break;
                
            case 'icon-size':
                const iconSize = content.querySelector('i');
                if (iconSize) iconSize.style.fontSize = value + 'px';
                break;
                
            case 'icon-color':
                const iconColor = content.querySelector('i');
                if (iconColor) iconColor.style.color = value;
                break;
                
            case 'icon-align':
                const iconAlign = content.querySelector('.widget-icon');
                if (iconAlign) iconAlign.style.textAlign = value;
                break;
                
            case 'video-src': {
                const embedUrl = this.applyVideoEmbed(content, value);
                const urlInput = document.querySelector('.elementor-settings-content input[data-setting="video-src"]');
                if (urlInput && embedUrl && embedUrl !== value.trim()) {
                    urlInput.value = embedUrl;
                }
                break;
            }

            case 'audio-src': {
                const audioPlayer = content.querySelector('.widget-audio-player');
                if (audioPlayer) {
                    audioPlayer.src = value;
                    audioPlayer.load();
                }
                break;
            }

            case 'audio-title': {
                const audioTitleEl = content.querySelector('.widget-audio-title');
                if (audioTitleEl) audioTitleEl.textContent = value;
                break;
            }

            case 'audio-desc': {
                const audioDescEl = content.querySelector('.widget-audio-desc');
                if (audioDescEl) audioDescEl.textContent = value;
                break;
            }

            case 'audio-autoplay': {
                const audioAutoplay = content.querySelector('.widget-audio-player');
                if (audioAutoplay) {
                    if (value === 'true') audioAutoplay.setAttribute('autoplay', '');
                    else audioAutoplay.removeAttribute('autoplay');
                }
                break;
            }

            case 'audio-loop': {
                const audioLoop = content.querySelector('.widget-audio-player');
                if (audioLoop) {
                    if (value === 'true') audioLoop.setAttribute('loop', '');
                    else audioLoop.removeAttribute('loop');
                }
                break;
            }
                
            case 'html-content':
                content.innerHTML = value;
                break;
                
            case 'margin-top':
                widget.style.marginTop = value + 'px';
                break;
                
            case 'margin-bottom':
                widget.style.marginBottom = value + 'px';
                break;
                
            case 'padding':
                content.style.padding = value + 'px';
                break;
        }
    }
    
    duplicateWidget(widgetId) {
        const widget = this.queryWidget(widgetId);
        if (!widget) return;

        const clone = widget.cloneNode(true);
        const newId = this.generateId();
        clone.dataset.id = newId;
        clone.classList.remove('active', 'elementor-widget-dragging');
        this.updateElementToolIds(clone, newId);

        widget.parentElement.insertBefore(clone, widget.nextSibling);
        this.saveState();
        this.editWidget(newId);
        this.showMessage('ویجت کپی شد', 'success');
    }

    removeWidget(widgetId, askConfirm = true) {
        if (askConfirm && !confirm('آیا از حذف این ویجت اطمینان دارید؟')) return;

        const widget = this.queryWidget(widgetId);
        if (!widget) {
            this.syncSelection();
            return;
        }

        const column = widget.closest('.elementor-column');
        const wasSelected = this.selectedElement === widget;

        widget.remove();

        if (column) this.ensureColumnPlaceholder(column);

        if (wasSelected) this.closeSettings();
        else this.syncSelection();

        this.saveState();
        this.showMessage('ویجت حذف شد', 'success');
    }

    duplicateSection(sectionId) {
        const section = this.querySection(sectionId);
        if (!section) return;

        const clone = section.cloneNode(true);
        const newId = this.generateId();
        clone.dataset.id = newId;
        clone.classList.remove('active', 'elementor-section-reverse-preview');
        this.updateElementToolIds(clone.querySelector('.elementor-section-tools'), newId);

        clone.querySelectorAll('.elementor-column').forEach(col => {
            const newColId = this.generateId();
            col.dataset.id = newColId;
            this.updateElementToolIds(col.querySelector('.elementor-column-header'), newColId);
        });

        clone.querySelectorAll('.elementor-widget').forEach(widget => {
            const newWidgetId = this.generateId();
            widget.dataset.id = newWidgetId;
            widget.classList.remove('active', 'elementor-widget-dragging');
            this.updateElementToolIds(widget.querySelector('.elementor-widget-tools'), newWidgetId);
        });

        section.parentElement.insertBefore(clone, section.nextSibling);
        this.applyResponsiveVars(clone);
        this.saveState();
        this.showMessage('بخش کپی شد', 'success');
    }

    removeSection(sectionId, askConfirm = true) {
        if (askConfirm && !confirm('آیا از حذف این بخش و تمام محتویات آن اطمینان دارید؟')) return;

        const section = this.querySection(sectionId);
        if (!section) {
            this.syncSelection();
            return;
        }

        const wasSelected = this.selectedElement === section || section.contains(this.selectedElement);
        section.remove();

        if (wasSelected) this.closeSettings();
        else this.syncSelection();

        this.saveState();
        this.showMessage('بخش حذف شد', 'success');

        const canvas = document.querySelector('.elementor-canvas-inner');
        if (canvas && canvas.querySelectorAll('.elementor-section').length === 0) {
            canvas.innerHTML = `
                <div class="elementor-empty-state">
                    <i class="fas fa-layer-group"></i>
                    <h3>صفحه خالی است</h3>
                    <p>برای شروع، یک بخش جدید اضافه کنید یا ویجت‌ها را بکشید و رها کنید</p>
                </div>
            `;
            this.ensureAddSectionButton();
        }
    }

    closeSettings() {
        this.commitInlineTextEdit();

        const widgetsView = document.querySelector('.elementor-panel-view-widgets');
        const settingsView = document.querySelector('.elementor-panel-view-settings');
        const settingsContent = document.querySelector('.elementor-settings-content');
        const subtitleEl = document.querySelector('.elementor-settings-subtitle');
        const panel = document.querySelector('.elementor-panel');

        widgetsView?.classList.remove('elementor-panel-hidden');
        settingsView?.classList.add('elementor-panel-hidden');
        panel?.classList.remove('showing-settings');

        document.querySelectorAll('.elementor-widget.active, .elementor-section.active, .elementor-column.active').forEach(el => {
            el.classList.remove('active');
        });

        if (settingsContent) {
            settingsContent.innerHTML = `
                <p class="elementor-settings-empty">
                    <i class="fas fa-mouse-pointer"></i>
                    یک بخش، ستون یا ویجت را از بوم انتخاب کنید
                </p>`;
        }
        if (subtitleEl) subtitleEl.textContent = '';

        this.selectedElement = null;
    }
    
    changeDeviceMode(device) {
        this.deviceMode = device;
        
        document.querySelectorAll('.elementor-device-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.device === device);
        });
        
        const canvas = document.querySelector('.elementor-canvas');
        canvas.className = 'elementor-canvas';
        if (device !== 'desktop') {
            canvas.classList.add(`device-${device}`);
        }

        this.applyDevicePreview();

        if (this.selectedElement?.classList.contains('elementor-column')) {
            this.editColumn(this.selectedElement.dataset.id);
        } else if (this.selectedElement?.classList.contains('elementor-section')) {
            this.editSection(this.selectedElement.dataset.id);
        } else if (this.selectedElement?.classList.contains('elementor-widget')) {
            this.editWidget(this.selectedElement.dataset.id);
        }
    }
    
    save() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        const content = this.sanitizeForSave(canvas);
        
        const contentField = this.getContentField();
        if (contentField) {
            contentField.value = content;
        }
        
        localStorage.setItem(this.config.draftKey, content);
    }
    
    preview() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        const content = this.sanitizeForSave(canvas);
        const contentCss = this.config.contentCssPath || this.config.cssPath;
        const faPath = this.config.fontAwesomePath || '';
        
        const previewWindow = window.open('', 'preview', 'width=1200,height=800');
        previewWindow.document.write(`
            <!DOCTYPE html>
            <html lang="fa" dir="rtl">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>پیش‌نمایش</title>
                ${faPath ? `<link rel="stylesheet" href="${faPath}">` : ''}
                <link rel="stylesheet" href="${contentCss}">
                <style>body { padding: 20px; font-family: Tahoma, sans-serif; }</style>
            </head>
            <body>
                <div class="elementor-content-root">${content}</div>
            </body>
            </html>
        `);
        previewWindow.document.close();
    }
    
    undo() {
        if (this.historyIndex > 0) {
            this.historyIndex--;
            this.restoreState(this.history[this.historyIndex]);
            this.showMessage('بازگشت انجام شد', 'success');
        }
    }
    
    redo() {
        if (this.historyIndex < this.history.length - 1) {
            this.historyIndex++;
            this.restoreState(this.history[this.historyIndex]);
            this.showMessage('انجام مجدد', 'success');
        }
    }
    
    clear() {
        if (!confirm('آیا از پاک کردن تمام محتوا اطمینان دارید؟')) return;
        
        const canvas = document.querySelector('.elementor-canvas-inner');
        canvas.innerHTML = `
            <div class="elementor-empty-state">
                <i class="fas fa-layer-group"></i>
                <h3>صفحه خالی است</h3>
                <p>برای شروع، یک بخش جدید اضافه کنید یا ویجت‌ها را بکشید و رها کنید</p>
            </div>
        `;
        this.ensureAddSectionButton();
        
        this.closeSettings();
        this.saveState();
        this.showMessage('محتوا پاک شد', 'success');
    }
    
    copy() {
        if (!this.selectedElement) return;
        this.clipboard = this.selectedElement.cloneNode(true);
        this.showMessage('کپی شد', 'success');
    }
    
    paste() {
        if (!this.clipboard) return;
        
        const clone = this.clipboard.cloneNode(true);
        const newId = this.generateId();
        clone.dataset.id = newId;
        
        if (this.selectedElement) {
            this.selectedElement.parentElement.insertBefore(clone, this.selectedElement.nextSibling);
        }
        
        this.saveState();
        this.showMessage('الصاق شد', 'success');
    }
    
    saveState() {
        const canvas = document.querySelector('.elementor-canvas-inner');
        let state;
        if (this.textEditor && this._inlineTextShell) {
            const clone = canvas.cloneNode(true);
            this.normalizeInlineEditorsInClone(clone);
            state = clone.innerHTML;
        } else {
            state = canvas.innerHTML;
        }
        
        // Remove future history if we're not at the end
        if (this.historyIndex < this.history.length - 1) {
            this.history = this.history.slice(0, this.historyIndex + 1);
        }
        
        this.history.push(state);
        this.historyIndex = this.history.length - 1;
        
        // Limit history to 50 states
        if (this.history.length > 50) {
            this.history.shift();
            this.historyIndex--;
        }
        
        // Auto-save
        this.save();
    }
    
    restoreState(state) {
        this.clearInlineTextEditorRefs();
        const canvas = document.querySelector('.elementor-canvas-inner');
        canvas.innerHTML = state;
        this.hydrateEditorContent();
        this.closeSettings();
    }
    
    loadExistingContent() {
        const contentField = this.getContentField();
        const canvas = document.querySelector('.elementor-canvas-inner');
        if (!canvas) return;
        
        if (contentField && contentField.value.trim()) {
            canvas.innerHTML = contentField.value;
            this.hydrateEditorContent();
            this.saveState();
        } else {
            const draft = localStorage.getItem(this.config.draftKey);
            if (draft && confirm('یک پیش‌نویس ذخیره شده وجود دارد. آیا می‌خواهید آن را بارگذاری کنید؟')) {
                canvas.innerHTML = draft;
                this.hydrateEditorContent();
                this.saveState();
            }
        }
    }
    
    switchTab(tab) {
        document.querySelectorAll('.elementor-panel-tab').forEach(t => t.classList.remove('active'));
        document.querySelector(`[data-tab="${tab}"]`)?.classList.add('active');
        
        document.querySelector('.elementor-panel-widgets')?.classList.toggle('elementor-panel-hidden', tab !== 'widgets');
        document.querySelector('.elementor-panel-templates')?.classList.toggle('elementor-panel-hidden', tab !== 'templates');
    }
    
    generateId() {
        return 'el-' + Math.random().toString(36).substr(2, 9);
    }
    
    getWidgetName(type) {
        const widget = this.widgets.find(w => w.id === type);
        return widget ? widget.name : type;
    }
    
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text ?? '';
        return div.innerHTML;
    }

    escapeAttr(text) {
        return this.escapeHtml(text).replace(/"/g, '&quot;');
    }

    destroyTextEditor() {
        if (this._textEditSaveTimer) {
            clearTimeout(this._textEditSaveTimer);
            this._textEditSaveTimer = null;
        }
        this.detachRteDockToolbar();
        this.textEditor = null;
        this._textEditorWidgetId = null;
        this._inlineTextShell = null;
        this._inlineEditType = null;
    }

    ensureRteDock() {
        let dock = document.getElementById('elementor-rte-dock');
        if (dock) return dock;

        const host = document.querySelector('.elementor-editor-host');
        const toolbar = host?.querySelector('.elementor-toolbar');
        dock = document.createElement('div');
        dock.id = 'elementor-rte-dock';
        dock.className = 'elementor-rte-dock';
        dock.hidden = true;
        dock.innerHTML = `
            <div class="elementor-rte-dock-label">
                <i class="fas fa-font"></i>
                <span>قالب‌بندی متن</span>
            </div>
            <div class="elementor-rte-dock-tools"></div>
        `;
        if (toolbar) toolbar.after(dock);
        else host?.prepend(dock);
        return dock;
    }

    detachRteDockToolbar() {
        this._rtePickerCleanup?.();
        this._rteHeaderMenuCleanup?.();
        const dock = document.getElementById('elementor-rte-dock');
        const tools = dock?.querySelector('.elementor-rte-dock-tools');
        if (tools) tools.replaceChildren();
        if (dock) {
            dock.hidden = true;
            dock.classList.remove('is-active');
        }
        document.querySelector('.elementor-editor-host')?.classList.remove('has-rte-dock');
        this._floatingToolbarHost = null;
    }

    attachRteDockToolbar(toolbarEl) {
        this.detachRteDockToolbar();
        if (!toolbarEl) return;

        const dock = this.ensureRteDock();
        const tools = dock.querySelector('.elementor-rte-dock-tools');
        if (!tools) return;

        tools.appendChild(toolbarEl);
        dock.hidden = false;
        dock.classList.add('is-active');
        document.querySelector('.elementor-editor-host')?.classList.add('has-rte-dock');
        this._floatingToolbarHost = dock;

        dock.addEventListener('mousedown', (e) => e.stopPropagation());
        dock.addEventListener('click', (e) => e.stopPropagation());
    }

    /** Quill Snow has no SVG for align=left — only for empty value. Inject the left-align icon. */
    fixAlignLeftIcon(toolbarEl) {
        const btn = toolbarEl?.querySelector('button.ql-align[value="left"]');
        if (!btn) return;
        if (btn.querySelector('.ql-stroke, .ql-fill')) return;
        btn.innerHTML = [
            '<svg viewBox="0 0 18 18">',
            '<line class="ql-stroke" x1="3" x2="15" y1="4" y2="4"></line>',
            '<line class="ql-stroke" x1="3" x2="11" y1="9" y2="9"></line>',
            '<line class="ql-stroke" x1="3" x2="15" y1="14" y2="14"></line>',
            '</svg>'
        ].join('');
    }

    mountRteSizeStepper(toolbarEl, quill) {
        if (!toolbarEl || !quill || toolbarEl.querySelector('.elementor-rte-size-stepper')) return;

        const wrap = document.createElement('span');
        wrap.className = 'ql-formats elementor-rte-size-stepper';
        wrap.innerHTML = [
            '<button type="button" class="elementor-rte-size-btn" data-delta="-1" title="کوچک‌تر" aria-label="کوچک‌تر">−</button>',
            `<input type="number" class="elementor-rte-size-input" min="${ELEMENTOR_QUILL_SIZE_MIN}" max="${ELEMENTOR_QUILL_SIZE_MAX}" step="1" value="16" title="اندازه فونت (px)" aria-label="اندازه فونت" />`,
            '<button type="button" class="elementor-rte-size-btn" data-delta="1" title="بزرگ‌تر" aria-label="بزرگ‌تر">+</button>'
        ].join('');

        const headerSelect = toolbarEl.querySelector('.elementor-rte-header-select');
        if (headerSelect) headerSelect.after(wrap);
        else toolbarEl.prepend(wrap);

        const input = wrap.querySelector('.elementor-rte-size-input');

        const clampSize = (raw) => {
            let n = parseInt(raw, 10);
            if (!Number.isFinite(n)) n = 16;
            return Math.min(ELEMENTOR_QUILL_SIZE_MAX, Math.max(ELEMENTOR_QUILL_SIZE_MIN, n));
        };

        const applySize = (raw) => {
            const n = clampSize(raw);
            input.value = String(n);
            const range = quill.getSelection(true);
            quill.format('size', `${n}px`);
            if (range) quill.setSelection(range.index, range.length, 'silent');
        };

        wrap.querySelectorAll('[data-delta]').forEach(btn => {
            btn.addEventListener('mousedown', (e) => {
                e.preventDefault();
                e.stopPropagation();
            });
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                applySize((parseInt(input.value, 10) || 16) + parseInt(btn.dataset.delta, 10));
            });
        });

        input.addEventListener('mousedown', (e) => e.stopPropagation());
        input.addEventListener('click', (e) => e.stopPropagation());
        input.addEventListener('change', () => applySize(input.value));
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                applySize(input.value);
                quill.focus();
            }
        });

        const syncFromSelection = () => {
            const size = quill.getFormat()?.size;
            if (typeof size === 'string' && size.endsWith('px')) {
                input.value = String(clampSize(size));
            }
        };
        quill.on('selection-change', syncFromSelection);
        quill.on('editor-change', syncFromSelection);
    }

    /** Custom header dropdown — Quill's built-in picker breaks in RTL dock. */
    mountRteHeaderSelect(toolbarEl, quill) {
        if (!toolbarEl || !quill || toolbarEl.querySelector('.elementor-rte-header-select')) return;

        const wrap = document.createElement('span');
        wrap.className = 'ql-formats elementor-rte-header-select';
        wrap.innerHTML = [
            '<button type="button" class="elementor-rte-header-btn" aria-haspopup="listbox" aria-expanded="false" title="سبک پاراگراف">',
            '<span class="elementor-rte-header-label">عادی</span>',
            '<i class="fas fa-chevron-down" aria-hidden="true"></i>',
            '</button>'
        ].join('');
        toolbarEl.prepend(wrap);

        const btn = wrap.querySelector('.elementor-rte-header-btn');
        const labelEl = wrap.querySelector('.elementor-rte-header-label');

        const menu = document.createElement('div');
        menu.className = 'elementor-rte-header-menu';
        menu.hidden = true;
        menu.setAttribute('role', 'listbox');
        menu.innerHTML = ELEMENTOR_QUILL_HEADERS.map(h => {
            const val = h.value === false ? '' : String(h.value);
            return `<button type="button" class="elementor-rte-header-option" role="option" data-value="${val}">${h.label}</button>`;
        }).join('');
        document.body.appendChild(menu);

        let isOpen = false;

        const closeMenu = () => {
            isOpen = false;
            menu.hidden = true;
            btn.setAttribute('aria-expanded', 'false');
            btn.classList.remove('is-open');
        };

        const positionMenu = () => {
            if (!menu.isConnected) document.body.appendChild(menu);

            const rect = btn.getBoundingClientRect();
            const margin = 8;
            const gap = 4;
            const mw = Math.max(rect.width, 148);

            menu.hidden = false;
            menu.style.cssText = [
                'position:fixed',
                `z-index:10100`,
                `min-width:${mw}px`,
                'right:auto',
                'bottom:auto',
                'margin:0'
            ].join(';');

            const oh = menu.offsetHeight || 180;
            let top = rect.bottom + gap;
            if (top + oh > window.innerHeight - margin) {
                top = Math.max(margin, rect.top - gap - oh);
            }
            let left = rect.right - mw;
            if (left < margin) left = margin;
            if (left + mw > window.innerWidth - margin) {
                left = Math.max(margin, window.innerWidth - margin - mw);
            }
            menu.style.top = `${Math.round(top)}px`;
            menu.style.left = `${Math.round(left)}px`;
        };

        const openMenu = () => {
            isOpen = true;
            positionMenu();
            btn.setAttribute('aria-expanded', 'true');
            btn.classList.add('is-open');
            const current = labelEl.textContent?.trim();
            menu.querySelectorAll('.elementor-rte-header-option').forEach(opt => {
                opt.classList.toggle('is-active', opt.textContent.trim() === current);
            });
        };

        const toggleMenu = (e) => {
            e.preventDefault();
            e.stopPropagation();
            if (isOpen) closeMenu();
            else openMenu();
        };

        btn.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();
        });
        btn.addEventListener('click', toggleMenu);

        menu.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();
        });

        menu.querySelectorAll('.elementor-rte-header-option').forEach(opt => {
            opt.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                const raw = opt.dataset.value;
                const value = raw === '' ? false : parseInt(raw, 10);
                const range = quill.getSelection(true);
                quill.format('header', value);
                if (range) quill.setSelection(range.index, range.length, 'silent');
                labelEl.textContent = opt.textContent.trim();
                closeMenu();
                quill.focus();
            });
        });

        const onDocPointer = (e) => {
            if (!isOpen) return;
            if (menu.contains(e.target) || wrap.contains(e.target)) return;
            closeMenu();
        };
        const onScrollOrResize = () => {
            if (isOpen) positionMenu();
        };
        // Use bubble phase so the button click can open first without racing a capture closer
        document.addEventListener('mousedown', onDocPointer);
        window.addEventListener('scroll', onScrollOrResize, true);
        window.addEventListener('resize', onScrollOrResize);

        const labelForHeader = (header) => {
            const match = ELEMENTOR_QUILL_HEADERS.find(h =>
                h.value === header || (h.value === false && (header === false || header == null))
            );
            return match?.label || 'عادی';
        };

        const syncFromSelection = () => {
            labelEl.textContent = labelForHeader(quill.getFormat()?.header);
        };
        quill.on('selection-change', syncFromSelection);
        quill.on('editor-change', syncFromSelection);

        this._rteHeaderMenuCleanup?.();
        this._rteHeaderMenuCleanup = () => {
            closeMenu();
            menu.remove();
            document.removeEventListener('mousedown', onDocPointer);
            window.removeEventListener('scroll', onScrollOrResize, true);
            window.removeEventListener('resize', onScrollOrResize);
            this._rteHeaderMenuCleanup = null;
        };
    }

    enhanceRteToolbar(toolbarEl, quill) {
        if (!toolbarEl) return;
        this.fixAlignLeftIcon(toolbarEl);
        this.mountRteHeaderSelect(toolbarEl, quill);
        this.mountRteSizeStepper(toolbarEl, quill);
        this.mountRteFixedPickers(toolbarEl);
    }

    /**
     * Quill picker menus (header / color) open with position:absolute and get
     * clipped under the settings panel. Pin them with position:fixed like the
     * admin category dropdown.
     */
    mountRteFixedPickers(toolbarEl) {
        if (!toolbarEl || toolbarEl.dataset.fixedPickers === '1') return;
        toolbarEl.dataset.fixedPickers = '1';

        const clearOptionsStyle = (options) => {
            if (!options) return;
            ['position', 'top', 'left', 'right', 'bottom', 'width', 'minWidth',
                'maxHeight', 'zIndex', 'margin', 'overflowY', 'visibility'].forEach(prop => {
                options.style[prop] = '';
            });
        };

        const positionOptions = (picker) => {
            const options = picker.querySelector(':scope > .ql-picker-options');
            const label = picker.querySelector(':scope > .ql-picker-label');
            if (!options || !label) return;

            if (!picker.classList.contains('ql-expanded')) {
                clearOptionsStyle(options);
                return;
            }

            const rect = label.getBoundingClientRect();
            const margin = 8;
            const gap = 4;
            const vw = window.innerWidth;
            const vh = window.innerHeight;

            options.style.position = 'fixed';
            options.style.zIndex = '10090';
            options.style.right = 'auto';
            options.style.bottom = 'auto';
            options.style.margin = '0';
            options.style.minWidth = `${Math.max(Math.ceil(rect.width), 128)}px`;
            options.style.visibility = 'hidden';

            requestAnimationFrame(() => {
                if (!picker.classList.contains('ql-expanded')) {
                    clearOptionsStyle(options);
                    return;
                }

                const ow = Math.max(options.offsetWidth || 0, rect.width, 128);
                const oh = options.offsetHeight || 160;

                let top = rect.bottom + gap;
                if (top + oh > vh - margin) {
                    top = Math.max(margin, rect.top - gap - oh);
                }

                // Align to label's inline-end (right edge in LTR metrics = natural under RTL chip)
                let left = rect.right - ow;
                if (left < margin) left = margin;
                if (left + ow > vw - margin) left = Math.max(margin, vw - margin - ow);

                options.style.top = `${Math.round(top)}px`;
                options.style.left = `${Math.round(left)}px`;
                options.style.maxHeight = `${Math.max(120, Math.min(280, vh - top - margin))}px`;
                options.style.overflowY = 'auto';
                options.style.visibility = '';
            });
        };

        const repositionExpanded = () => {
            toolbarEl.querySelectorAll('.ql-picker.ql-expanded').forEach(positionOptions);
        };

        const observer = new MutationObserver(mutations => {
            for (const m of mutations) {
                if (m.type === 'attributes' && m.target?.classList?.contains('ql-picker')) {
                    positionOptions(m.target);
                }
            }
        });

        toolbarEl.querySelectorAll('.ql-picker').forEach(picker => {
            observer.observe(picker, { attributes: true, attributeFilter: ['class'] });
        });

        const onScrollOrResize = () => repositionExpanded();
        window.addEventListener('scroll', onScrollOrResize, true);
        window.addEventListener('resize', onScrollOrResize);

        this._rtePickerCleanup?.();
        this._rtePickerCleanup = () => {
            observer.disconnect();
            window.removeEventListener('scroll', onScrollOrResize, true);
            window.removeEventListener('resize', onScrollOrResize);
            toolbarEl.querySelectorAll('.ql-picker-options').forEach(clearOptionsStyle);
            delete toolbarEl.dataset.fixedPickers;
            this._rtePickerCleanup = null;
        };
    }

    clearInlineTextEditorRefs() {
        document.querySelectorAll('.elementor-widget.is-inline-editing').forEach(el => {
            el.classList.remove('is-inline-editing');
        });
        document.querySelectorAll('.elementor-inline-heading').forEach(el => {
            el.contentEditable = 'false';
            el.classList.remove('elementor-inline-heading');
            el.removeAttribute('contenteditable');
        });
        this.destroyTextEditor();
    }

    normalizeInlineEditorsInClone(root) {
        root.querySelectorAll('.elementor-inline-heading, [contenteditable="true"]').forEach(el => {
            el.contentEditable = 'false';
            el.classList.remove('elementor-inline-heading');
            el.removeAttribute('contenteditable');
        });

        root.querySelectorAll('.elementor-inline-text-editor').forEach(shell => {
            const type = shell.dataset.inlineType || 'text';
            const editor = shell.querySelector('.ql-editor') || shell.querySelector('.elementor-inline-text-fallback');
            const html = editor?.innerHTML || '<p><br></p>';
            let restored;

            if (type === 'quote') {
                restored = document.createElement('blockquote');
                restored.innerHTML = html;
            } else if (type === 'alert') {
                restored = document.createElement('div');
                restored.className = shell.dataset.restoreClass || 'widget-alert alert alert-info';
                restored.innerHTML = html;
            } else {
                restored = document.createElement('div');
                restored.className = 'widget-text';
                restored.innerHTML = html;
            }

            if (editor) {
                ['color', 'fontSize', 'textAlign', 'backgroundColor'].forEach(prop => {
                    if (editor.style[prop]) restored.style[prop] = editor.style[prop];
                });
            }

            shell.replaceWith(restored);
        });

        root.querySelectorAll('.ql-toolbar').forEach(el => el.remove());
        root.querySelectorAll('.elementor-widget.is-inline-editing').forEach(el => {
            el.classList.remove('is-inline-editing');
        });
    }

    applyInlineTextStyle(widgetId, prop, value) {
        if (this._textEditorWidgetId === widgetId && this.textEditor) {
            this.textEditor.root.style[prop] = value;
            return;
        }

        const widget = this.queryWidget(widgetId);
        const textRoot = widget?.querySelector('.widget-text, .widget-alert, blockquote, h1, h2, h3, h4, h5, h6');
        if (textRoot) textRoot.style[prop] = value;
    }

    commitInlineTextEdit() {
        if (!this.textEditor || !this._textEditorWidgetId) {
            this.clearInlineTextEditorRefs();
            return;
        }

        const widgetId = this._textEditorWidgetId;
        const widget = this.queryWidget(widgetId);
        const type = this._inlineEditType || widget?.dataset.type || 'text';
        const editorRoot = this.textEditor.root;

        if (type === 'heading' || this.textEditor.isHeading) {
            if (editorRoot) {
                editorRoot.contentEditable = 'false';
                editorRoot.classList.remove('elementor-inline-heading');
                editorRoot.removeAttribute('contenteditable');
            }
            widget?.classList.remove('is-inline-editing');
            this.destroyTextEditor();
            if (!this._skipInlineHistory) {
                this._skipInlineHistory = true;
                this.saveState();
                this._skipInlineHistory = false;
            }
            return;
        }

        const shell = this._inlineTextShell;
        const html = editorRoot?.innerHTML || '<p><br></p>';
        let restored;

        if (type === 'quote') {
            restored = document.createElement('blockquote');
            restored.innerHTML = html;
        } else if (type === 'alert') {
            restored = document.createElement('div');
            restored.className = shell?.dataset.restoreClass || 'widget-alert alert alert-info';
            restored.innerHTML = html;
        } else {
            restored = document.createElement('div');
            restored.className = 'widget-text';
            restored.innerHTML = html;
        }

        ['color', 'fontSize', 'textAlign', 'backgroundColor'].forEach(prop => {
            if (editorRoot?.style[prop]) restored.style[prop] = editorRoot.style[prop];
        });

        if (shell?.parentElement) {
            shell.replaceWith(restored);
        } else if (widget) {
            const content = widget.querySelector('.elementor-widget-content');
            const existing = content?.querySelector('.widget-text, .widget-alert, blockquote, .elementor-inline-text-editor');
            if (existing) existing.replaceWith(restored);
            else if (content) {
                if (type === 'quote') {
                    let wrap = content.querySelector('.widget-quote');
                    if (!wrap) {
                        wrap = document.createElement('div');
                        wrap.className = 'widget-quote';
                        content.appendChild(wrap);
                    }
                    wrap.appendChild(restored);
                } else {
                    content.appendChild(restored);
                }
            }
        }

        widget?.classList.remove('is-inline-editing');
        this.destroyTextEditor();

        if (!this._skipInlineHistory) {
            this._skipInlineHistory = true;
            this.saveState();
            this._skipInlineHistory = false;
        }
    }

    startInlineTextEdit(widgetId) {
        const widget = this.queryWidget(widgetId);
        if (!widget || !ELEMENTOR_INLINE_TYPES.has(widget.dataset.type)) return;

        if (this._textEditorWidgetId === widgetId && this.textEditor) {
            this.textEditor.focus?.();
            return;
        }

        this._skipInlineHistory = true;
        this.commitInlineTextEdit();
        this._skipInlineHistory = false;

        const type = widget.dataset.type;
        const content = widget.querySelector('.elementor-widget-content');
        if (!content) return;

        if (type === 'heading') {
            this.startInlineHeadingEdit(widget, widgetId);
            return;
        }

        let target = null;
        let restoreClass = '';

        if (type === 'text') {
            target = content.querySelector('.widget-text');
            if (!target) {
                target = document.createElement('div');
                target.className = 'widget-text';
                target.innerHTML = '<p><br></p>';
                content.appendChild(target);
            }
        } else if (type === 'quote') {
            let wrap = content.querySelector('.widget-quote');
            if (!wrap) {
                wrap = document.createElement('div');
                wrap.className = 'widget-quote';
                content.appendChild(wrap);
            }
            target = wrap.querySelector('blockquote');
            if (!target) {
                target = document.createElement('blockquote');
                target.innerHTML = '<p>این یک نقل قول است</p>';
                wrap.appendChild(target);
            }
        } else if (type === 'alert') {
            target = content.querySelector('.widget-alert');
            if (!target) {
                target = document.createElement('div');
                target.className = 'widget-alert alert alert-info';
                target.innerHTML = '<p>این یک پیام هشدار است</p>';
                content.appendChild(target);
            }
            restoreClass = target.className;
        }

        if (!target) return;

        const initialHtml = target.innerHTML?.trim() || '<p><br></p>';

        const shell = document.createElement('div');
        shell.className = 'elementor-inline-text-editor';
        shell.dataset.inlineType = type;
        if (restoreClass) shell.dataset.restoreClass = restoreClass;

        const mount = document.createElement('div');
        mount.className = 'elementor-inline-text-mount';
        shell.appendChild(mount);
        target.replaceWith(shell);

        widget.classList.add('is-inline-editing');
        this._inlineTextShell = shell;
        this._textEditorWidgetId = widgetId;
        this._inlineEditType = type;

        const placeholder = type === 'quote'
            ? 'متن نقل قول را بنویسید...'
            : type === 'alert'
                ? 'متن هشدار را بنویسید...'
                : 'متن خود را اینجا بنویسید...';

        if (typeof Quill === 'undefined') {
            mount.contentEditable = 'true';
            mount.classList.add('elementor-inline-text-fallback');
            mount.innerHTML = initialHtml;
            mount.focus();
            this.textEditor = { root: mount, focus: () => mount.focus() };
            shell.addEventListener('mousedown', (e) => e.stopPropagation());
            shell.addEventListener('click', (e) => e.stopPropagation());
            return;
        }

        elementorEnsureQuillFormats();

        const quill = new Quill(mount, {
            theme: 'snow',
            placeholder,
            modules: {
                toolbar: {
                    container: ELEMENTOR_QUILL_TOOLBAR,
                    handlers: {
                        align(value) {
                            // Explicit handler so left/right/center always apply as styles
                            this.quill.format('align', value || false);
                        }
                    }
                }
            },
            formats: [
                'header', 'size', 'bold', 'italic', 'underline', 'strike',
                'color', 'background', 'script', 'list', 'indent',
                'align', 'direction', 'blockquote', 'code-block', 'link'
            ]
        });

        quill.root.setAttribute('dir', 'rtl');
        quill.setText('');
        quill.clipboard.dangerouslyPasteHTML(0, initialHtml);

        // Default Persian block direction + right align
        const len = quill.getLength();
        if (len > 0) {
            quill.formatLine(0, len, { direction: 'rtl', align: 'right' }, 'silent');
        }

        quill.focus();

        const toolbarEl = shell.querySelector('.ql-toolbar');
        if (toolbarEl) {
            // Attach first, then enhance — detach() would wipe header menu if enhanced before attach
            this.attachRteDockToolbar(toolbarEl);
            this.enhanceRteToolbar(toolbarEl, quill);
        }

        shell.addEventListener('mousedown', (e) => e.stopPropagation());
        shell.addEventListener('click', (e) => e.stopPropagation());

        this.textEditor = quill;
    }

    startInlineHeadingEdit(widget, widgetId) {
        const wrap = widget.querySelector('.widget-heading') || widget.querySelector('.elementor-widget-content');
        let heading = widget.querySelector('h1, h2, h3, h4, h5, h6');
        if (!heading && wrap) {
            heading = document.createElement('h2');
            heading.textContent = 'عنوان جدید';
            wrap.appendChild(heading);
        }
        if (!heading) return;

        heading.contentEditable = 'true';
        heading.classList.add('elementor-inline-heading');
        heading.spellcheck = true;
        heading.focus();

        const range = document.createRange();
        range.selectNodeContents(heading);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);

        heading.addEventListener('mousedown', (e) => e.stopPropagation());
        heading.addEventListener('click', (e) => e.stopPropagation());
        heading.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                document.execCommand('insertLineBreak');
            }
        });

        widget.classList.add('is-inline-editing');
        this._textEditorWidgetId = widgetId;
        this._inlineEditType = 'heading';
        this._inlineTextShell = null;
        this.textEditor = {
            root: heading,
            focus: () => heading.focus(),
            isHeading: true
        };
    }

    setupListSettingsListeners(widgetId) {
        const settingsPanel = document.querySelector('.elementor-settings-content');
        if (!settingsPanel?.querySelector('.elementor-list-editor')) return;

        const ensureList = () => {
            const widget = this.queryWidget(widgetId);
            if (!widget) return null;
            const wrap = widget.querySelector('.widget-list');
            if (!wrap) return null;
            let list = wrap.querySelector('ul, ol');
            if (!list) {
                list = document.createElement('ul');
                wrap.appendChild(list);
            }
            return { widget, wrap, list };
        };

        settingsPanel.querySelectorAll('.elementor-list-item-input').forEach(input => {
            input.addEventListener('input', () => {
                const ctx = ensureList();
                if (!ctx) return;
                const index = Number(input.dataset.index);
                const items = ctx.list.querySelectorAll(':scope > li');
                if (items[index]) {
                    items[index].textContent = input.value;
                }
            });
            input.addEventListener('change', () => this.saveState());
        });

        settingsPanel.querySelector('[data-action="list-add"]')?.addEventListener('click', () => {
            const ctx = ensureList();
            if (!ctx) return;
            const li = document.createElement('li');
            li.textContent = 'مورد جدید';
            ctx.list.appendChild(li);
            this.saveState();
            this.editWidget(widgetId);
        });

        settingsPanel.querySelectorAll('[data-action="list-remove"]').forEach(btn => {
            btn.addEventListener('click', () => {
                const ctx = ensureList();
                if (!ctx) return;
                const items = ctx.list.querySelectorAll(':scope > li');
                if (items.length <= 1) {
                    this.showMessage('حداقل یک مورد در لیست لازم است', 'error');
                    return;
                }
                const index = Number(btn.dataset.index);
                items[index]?.remove();
                this.saveState();
                this.editWidget(widgetId);
            });
        });
    }
    
    showMessage(message, type = 'success') {
        const existingMsg = document.querySelector('.elementor-message');
        if (existingMsg) existingMsg.remove();
        
        const msg = document.createElement('div');
        msg.className = `elementor-message ${type}`;
        msg.innerHTML = `
            <i class="fas fa-${type === 'success' ? 'check-circle' : 'exclamation-circle'}"></i>
            <span>${message}</span>
        `;
        
        document.body.appendChild(msg);
        
        setTimeout(() => msg.remove(), 3000);
    }
}

// Initialize when DOM is ready
let elementorBuilder;
document.addEventListener('DOMContentLoaded', () => {
    if (document.querySelector('.elementor-canvas-inner')) {
        elementorBuilder = new ElementorBuilder(window.ElementorBuilderConfig);
        window.elementorBuilder = elementorBuilder;
    }
});

