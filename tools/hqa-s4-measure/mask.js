'use strict';
// M8 [장치] 가림 — 설계 §5 · 고침1 F4 · F5
//   사용: node mask.js <원자료 폴더>
//   ① 러너 경로(GITHUB_WORKSPACE · RUNNER_TEMP · HOME 실값) → <러너>
//   ② 표준 base64 꼴(+ / = 를 포함 · 대문자 · 소문자 · 숫자가 다 있음 · 22자 이상) → <가림:n>   (F4)
//   ③ 영숫자 · '-' · '_' 22자 이상 토큰 → <가림:n>
//      단 40자 · 64자 16진은 「알려진 칸」 에 있을 때만 남긴다(F5 — 아래 hexAllowedAt):
//        JSON 키 HEX_KEYS 의 값 · 세부 글자의 key=값(HEX_KV) · MODE 글자 armed: 뒤 · SHA256SUMS.raw 줄머리
//        · 장치 고정값(KNOWN_HEX · expected-sums.txt · 환경 GITHUB_SHA)
//      (같은 토큰 = 같은 n · 값은 메모리에만 · 어디에도 안 씀)
//   ④ 텍스트 파일(.json · .jsonl · .txt)만 · PNG 는 leak-scan 이 머리 바이트 · 텍스트 덩어리를 본다(F6)
//   가린 개수는 run.json 의 mask 칸에 수로만. 종료 코드 0 = 끝 · 1 = 처리 오류
//   고침4 G7: ⑤ 판정 칸 스키마(rules.json judgeSchema · 파일 이름별)에 걸린 덩어리는 가리지 않는다 → judge_field_conflicts(값 0)
//             (판독기는 같은 꼴을 미리 재서 그 표본을 「판정 칸 가림 충돌」 device_failure 로 · 남은 값은 leak-scan 이 파일째 보류)
//             ⑥ 가림 기록 records = 파일 · 칸 경로 · 꼴 종류 · 길이(값 0 · 이름 조각이 꼴에 걸리면 <이름>)
//   고침5 H2-4(결-9 甲): MudBlazor 클래스 이름 정확 일치 목록 MUD_CLASS_ALLOW(꼴 허용 아님 · 목록 밖 mud-… 22자 이상 = 가림 그대로)
//   고침5 H2-2(결-7): 판독 규칙 · 판독기 파일의 sha256(이 나무 rules.json · judge.js 바이트) = 정확 일치 16진으로 남김(knownHex)
//   고침5 H2-3(결-6 甲): node mask.js <원자료 폴더> --only <상대 경로> [--summary-key <run.json 칸>]
//             = 그 파일만 가림(M8′ · verdict) · 요약은 run.json 의 다른 칸(기본 mask_verdict · 옛 mask 칸 덮어쓰기 0)
//             · <가림:n> 번호는 run.json mask.distinct_tokens 다음부터(앞 회차 번호와 겹침 0)
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const TEXT_EXT = new Set(['.json', '.jsonl', '.txt']);
const TOKEN_RE = /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{22,}(?![A-Za-z0-9_-])/g;
// 표준 base64: 고침2 Y2-a — 앞쪽 금지 = base64 몸 글자만 · 뒤쪽 금지 = 몸 글자 + '=' (= . - _ : ? & 뒤에서도 시작)
const B64_RE = /(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{22,}={0,2}(?![A-Za-z0-9+/=])/g;
// 고침2 Y2-b 오탐 경계: '/' 만으로 자격을 얻은 덩어리 중 낱말 경로 꼴(Components/After/CsStatusCell · content/MudBlazor/MudBlazor)은 뺀다
const PATH_SEG_RE = /^(?:[a-z]+|[A-Z][a-z]+)(?:[A-Z][a-z]+)*$/;
// 고침2 Y3: 16진 허용 칸은 장치가 쓰는 파일에서만(시험 앱 기록 안 x-main= 등은 가림)
const DEVICE_HEX_FILES = new Set(['m0-assert.json', 'run.json', 'SHA256SUMS.raw']);
const HEX_RE = /^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$/;
// 공개된 고정 이름만(릴리스 자산 · 꾸러미 · 환경 변수 이름). 값이 아니라 이름이다.
const FIXED_ALLOW = [
  'chrome-headless-shell-linux64',
  // 고침4 G9(PM F-6 ⑤): F′ 판 릴리스 · 자산 이름(옛 …-01161edb 대체)
  'hqa-s4-fixture-b967b1b0',
  'hqa-s4-fixture-linux-x64-b967b1b0',
  'hqa-s4-fixture-src-b967b1b0',
  'fonts-noto-color-emoji',
  'ASPNETCORE_ENVIRONMENT',
  'PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD',
];
// F5 — 16진을 남기는 칸. 장치 스크립트가 git SHA · sha256 을 쓰는 칸 이름만(m0-assert · boot · publish 가 쓰는 이름과 1:1).
const HEX_KEYS = new Set([
  'sha', 'head', 'device_commit', 'fixture_tree', 'fixture_tree_expected', 'base_expected', 'feat_expected',
  'fixture_blob', 'src_blob', 'tree_blob', 'raw_blob', 'norm_blob', 'sha256', 'commit',
]);
const HEX_KV = new Set([
  'got', 'want', 'expected', 'release', 'HEAD', 'event', 'parent', 'mode', 'tree', 'base', 'feat', 'fixture', 'main',
]);
// 장치 원문에 이미 공개된 고정 SHA(시험 앱 나무 · 시험 앱 머리 · 기능 머리)
//   고침4 G9(PM F-6 ⑤): 시험 앱 나무 · 머리 = F′ 값(옛 0b31f869 대체) · 01161edb = F′ 의 부모(m0 조상 단언 유지)
const KNOWN_HEX = [
  '47566f04d18ac9fc617a6bfc0dd0ff7c93efb5f8',
  'b967b1b007fb519d9a91537fbef911e818e950de',
  '01161edb5618b5e34ea2c5e655dcaaf2333d3a68',
  '16e88c01092d33eb643c6cac454703f00efa02ac',
];

// 고침5 H2-4(결-9 甲) — MudBlazor 클래스 이름 정확 일치 목록(641개 · 장치 밖에서 미리 뽑아 고정 · 리허설 원자료를 보고 넣은 값 아님)
//   출처: 릴리스 hqa-s4-fixture-b967b1b0 자산 hqa-s4-fixture-linux-x64-b967b1b0.tar.gz
//         (sha256 ed3a6141b8bff77e7969f212a50153b8b6b07ad91d2790a276739cb59435d230 = expected-sums.txt 와 같음)
//         안의 wwwroot/_content/MudBlazor/MudBlazor.min.css (sha256 e14e158cddb262e4afd212568dfacfb3d89216c6795b46225d1e004271642c64)
//   뽑는 명령: tar -xzf <자산> hqa-s4-fixture-linux-x64-b967b1b0/wwwroot/_content/MudBlazor/MudBlazor.min.css
//             grep -oE '\.mud-[A-Za-z0-9_-]+' MudBlazor.min.css | sed 's/^\.//' | LC_ALL=C sort -u | awk 'length($0)>=22'
//   목록 지문: 이름을 줄바꿈으로 잇고 끝 줄바꿈 1 의 sha256 = MUD_CLASS_ALLOW_SHA256 (selftest-leak.js 가 개수 · 지문 단언)
const MUD_CLASS_ALLOW_SHA256 = 'efeadc7725f9d64b7a0ccbba6a2f8d15f6a1194e16a746759b38361f529ef1b6';
const MUD_CLASS_ALLOW = [
  'mud-alert-filled-error', 'mud-alert-filled-normal', 'mud-alert-filled-primary', 'mud-alert-filled-secondary',
  'mud-alert-filled-success', 'mud-alert-filled-tertiary', 'mud-alert-filled-warning', 'mud-alert-outlined-dark',
  'mud-alert-outlined-error', 'mud-alert-outlined-info', 'mud-alert-outlined-normal', 'mud-alert-outlined-primary',
  'mud-alert-outlined-secondary', 'mud-alert-outlined-success', 'mud-alert-outlined-tertiary', 'mud-alert-outlined-warning',
  'mud-alert-text-primary', 'mud-alert-text-secondary', 'mud-alert-text-success', 'mud-alert-text-tertiary', 'mud-alert-text-warning',
  'mud-appbar-fixed-bottom', 'mud-application-layout-rtl', 'mud-autocomplete--with-progress', 'mud-autocomplete-after-items',
  'mud-autocomplete-before-items', 'mud-autocomplete-more-items', 'mud-autocomplete-no-items', 'mud-avatar-filled-dark',
  'mud-avatar-filled-error', 'mud-avatar-filled-info', 'mud-avatar-filled-primary', 'mud-avatar-filled-secondary',
  'mud-avatar-filled-success', 'mud-avatar-filled-tertiary', 'mud-avatar-filled-warning', 'mud-avatar-group-outlined',
  'mud-avatar-group-outlined-dark', 'mud-avatar-group-outlined-error', 'mud-avatar-group-outlined-info',
  'mud-avatar-group-outlined-primary', 'mud-avatar-group-outlined-secondary', 'mud-avatar-group-outlined-success',
  'mud-avatar-group-outlined-surface', 'mud-avatar-group-outlined-tertiary', 'mud-avatar-group-outlined-transparent',
  'mud-avatar-group-outlined-warning', 'mud-avatar-outlined-dark', 'mud-avatar-outlined-error', 'mud-avatar-outlined-info',
  'mud-avatar-outlined-primary', 'mud-avatar-outlined-secondary', 'mud-avatar-outlined-success', 'mud-avatar-outlined-tertiary',
  'mud-avatar-outlined-warning', 'mud-border-lines-default', 'mud-breadcrumb-separator', 'mud-breadcrumbs-expander',
  'mud-button-color-inherit', 'mud-button-disable-elevation', 'mud-button-filled-dark', 'mud-button-filled-error',
  'mud-button-filled-info', 'mud-button-filled-primary', 'mud-button-filled-secondary', 'mud-button-filled-size-large',
  'mud-button-filled-size-small', 'mud-button-filled-success', 'mud-button-filled-tertiary', 'mud-button-filled-warning',
  'mud-button-group-disable-elevation', 'mud-button-group-filled', 'mud-button-group-filled-dark', 'mud-button-group-filled-error',
  'mud-button-group-filled-info', 'mud-button-group-filled-primary', 'mud-button-group-filled-secondary',
  'mud-button-group-filled-size-large', 'mud-button-group-filled-size-small', 'mud-button-group-filled-success',
  'mud-button-group-filled-tertiary', 'mud-button-group-filled-warning', 'mud-button-group-horizontal', 'mud-button-group-outlined',
  'mud-button-group-outlined-dark', 'mud-button-group-outlined-error', 'mud-button-group-outlined-info',
  'mud-button-group-outlined-primary', 'mud-button-group-outlined-secondary', 'mud-button-group-outlined-size-large',
  'mud-button-group-outlined-size-small', 'mud-button-group-outlined-success', 'mud-button-group-outlined-tertiary',
  'mud-button-group-outlined-warning', 'mud-button-group-override-styles', 'mud-button-group-text-dark', 'mud-button-group-text-error',
  'mud-button-group-text-info', 'mud-button-group-text-primary', 'mud-button-group-text-secondary', 'mud-button-group-text-size-large',
  'mud-button-group-text-size-small', 'mud-button-group-text-success', 'mud-button-group-text-tertiary', 'mud-button-group-text-warning',
  'mud-button-group-vertical', 'mud-button-icon-size-large', 'mud-button-icon-size-medium', 'mud-button-icon-size-small',
  'mud-button-outlined-dark', 'mud-button-outlined-default', 'mud-button-outlined-error', 'mud-button-outlined-info',
  'mud-button-outlined-inherit', 'mud-button-outlined-primary', 'mud-button-outlined-secondary', 'mud-button-outlined-size-large',
  'mud-button-outlined-size-small', 'mud-button-outlined-success', 'mud-button-outlined-tertiary', 'mud-button-outlined-warning',
  'mud-button-text-inherit', 'mud-button-text-primary', 'mud-button-text-secondary', 'mud-button-text-size-large',
  'mud-button-text-size-small', 'mud-button-text-success', 'mud-button-text-tertiary', 'mud-button-text-warning',
  'mud-card-actions-padding', 'mud-card-content-padding', 'mud-card-header-actions', 'mud-card-header-avatar', 'mud-card-header-content',
  'mud-card-header-padding', 'mud-carousel-elements-rtl', 'mud-carousel-item-dark', 'mud-carousel-item-error', 'mud-carousel-item-exit',
  'mud-carousel-item-info', 'mud-carousel-item-primary', 'mud-carousel-item-secondary', 'mud-carousel-item-success',
  'mud-carousel-item-tertiary', 'mud-carousel-item-warning', 'mud-carousel-secondary', 'mud-carousel-transition-fade-in',
  'mud-carousel-transition-fade-out', 'mud-carousel-transition-none', 'mud-carousel-transition-slide-next-enter',
  'mud-carousel-transition-slide-next-exit', 'mud-carousel-transition-slide-next-rtl-enter', 'mud-carousel-transition-slide-next-rtl-exit',
  'mud-carousel-transition-slide-prev-enter', 'mud-carousel-transition-slide-prev-exit', 'mud-carousel-transition-slide-prev-rtl-enter',
  'mud-carousel-transition-slide-prev-rtl-exit', 'mud-chart-heatmap-legend', 'mud-chart-legend-bottom', 'mud-chart-legend-checkbox',
  'mud-chart-legend-marker', 'mud-chart-legend-right', 'mud-chart-marker-color-0', 'mud-chart-marker-color-1', 'mud-chart-marker-color-2',
  'mud-chart-marker-color-3', 'mud-chart-marker-color-4', 'mud-chip-color-primary', 'mud-chip-color-secondary', 'mud-chip-color-success',
  'mud-chip-color-tertiary', 'mud-chip-color-warning', 'mud-collapse-container', 'mud-collapse-wrapper-inner', 'mud-container--gutters',
  'mud-container-maxwidth-lg', 'mud-container-maxwidth-md', 'mud-container-maxwidth-sm', 'mud-container-maxwidth-xl',
  'mud-container-maxwidth-xs', 'mud-container-maxwidth-xxl', 'mud-data-grid-columns-panel', 'mud-dialog-bottomcenter',
  'mud-dialog-bottomright', 'mud-dialog-centerright', 'mud-dialog-no-side-padding', 'mud-dialog-width-false', 'mud-drawer-clipped-always',
  'mud-drawer-clipped-docked', 'mud-drawer-clipped-never', 'mud-drawer-close-mini-always', 'mud-drawer-close-mini-always-left',
  'mud-drawer-close-mini-always-right', 'mud-drawer-close-mini-lg-left', 'mud-drawer-close-mini-lg-right', 'mud-drawer-close-mini-md-left',
  'mud-drawer-close-mini-md-right', 'mud-drawer-close-mini-none', 'mud-drawer-close-mini-none-left', 'mud-drawer-close-mini-none-right',
  'mud-drawer-close-mini-sm-left', 'mud-drawer-close-mini-sm-right', 'mud-drawer-close-mini-xl-left', 'mud-drawer-close-mini-xl-right',
  'mud-drawer-close-mini-xs-left', 'mud-drawer-close-mini-xs-right', 'mud-drawer-close-mini-xxl-left', 'mud-drawer-close-mini-xxl-right',
  'mud-drawer-header-dense', 'mud-drawer-left-clipped-docked', 'mud-drawer-left-clipped-never', 'mud-drawer-open-mini-always',
  'mud-drawer-open-mini-always-left', 'mud-drawer-open-mini-always-right', 'mud-drawer-open-mini-lg-left', 'mud-drawer-open-mini-lg-right',
  'mud-drawer-open-mini-md-left', 'mud-drawer-open-mini-md-right', 'mud-drawer-open-mini-none', 'mud-drawer-open-mini-none-left',
  'mud-drawer-open-mini-none-right', 'mud-drawer-open-mini-sm-left', 'mud-drawer-open-mini-sm-right', 'mud-drawer-open-mini-xl-left',
  'mud-drawer-open-mini-xl-right', 'mud-drawer-open-mini-xs-left', 'mud-drawer-open-mini-xs-right', 'mud-drawer-open-mini-xxl-left',
  'mud-drawer-open-mini-xxl-right', 'mud-drawer-open-persistent-left', 'mud-drawer-open-persistent-right',
  'mud-drawer-open-responsive-lg-left', 'mud-drawer-open-responsive-lg-right', 'mud-drawer-open-responsive-md-left',
  'mud-drawer-open-responsive-md-right', 'mud-drawer-open-responsive-sm-left', 'mud-drawer-open-responsive-sm-right',
  'mud-drawer-open-responsive-xl-left', 'mud-drawer-open-responsive-xl-right', 'mud-drawer-open-responsive-xs-left',
  'mud-drawer-open-responsive-xs-right', 'mud-drawer-open-responsive-xxl-left', 'mud-drawer-open-responsive-xxl-right',
  'mud-drawer-overlay--open', 'mud-drawer-overlay-mini', 'mud-drawer-overlay-responsive', 'mud-drawer-overlay-temporary',
  'mud-drawer-overlay-xxl', 'mud-drawer-right-clipped-docked', 'mud-drawer-right-clipped-never', 'mud-drop-item-draggable',
  'mud-drop-item-preview-start', 'mud-drop-zone-can-drop', 'mud-drop-zone-drag-block', 'mud-expand-panel-border',
  'mud-expand-panel-content', 'mud-expand-panel-dense', 'mud-expand-panel-gutters', 'mud-expand-panel-header',
  'mud-expand-panel-header-gutters', 'mud-expansion-panels-borders', 'mud-expansion-panels-square', 'mud-fab-anchor-bottom-center',
  'mud-fab-anchor-bottom-left', 'mud-fab-anchor-bottom-right', 'mud-fab-anchor-center-center', 'mud-fab-anchor-center-left',
  'mud-fab-anchor-center-right', 'mud-fab-anchor-top-center', 'mud-fab-anchor-top-left', 'mud-fab-anchor-top-right',
  'mud-fab-disable-elevation', 'mud-fab-menu-container', 'mud-fab-menu-direction-bottom', 'mud-fab-menu-direction-end',
  'mud-fab-menu-direction-left', 'mud-fab-menu-direction-right', 'mud-fab-menu-direction-start', 'mud-fab-menu-direction-top',
  'mud-file-upload-custom-content', 'mud-file-upload-dragarea', 'mud-file-upload-dragarea-clickable', 'mud-file-upload-dragover',
  'mud-file-upload-files-default-template', 'mud-focus-trap-child-container', 'mud-grid-item-xxl-auto', 'mud-grid-item-xxl-true',
  'mud-grid-spacing-xs-10', 'mud-grid-spacing-xs-11', 'mud-grid-spacing-xs-12', 'mud-grid-spacing-xs-13', 'mud-grid-spacing-xs-14',
  'mud-grid-spacing-xs-15', 'mud-grid-spacing-xs-16', 'mud-grid-spacing-xs-17', 'mud-grid-spacing-xs-18', 'mud-grid-spacing-xs-19',
  'mud-grid-spacing-xs-20', 'mud-header-togglehierarchy', 'mud-icon-button-color-inherit', 'mud-icon-button-edge-end',
  'mud-icon-button-edge-margin-end', 'mud-icon-button-edge-start', 'mud-icon-button-size-large', 'mud-icon-button-size-small',
  'mud-input-adorned-start', 'mud-input-adornment-disable-pointerevents', 'mud-input-adornment-end', 'mud-input-adornment-start',
  'mud-input-content-placement-bottom', 'mud-input-content-placement-end', 'mud-input-content-placement-start',
  'mud-input-content-placement-top', 'mud-input-control-boolean-input', 'mud-input-control-full-width',
  'mud-input-control-helper-container', 'mud-input-control-input-container', 'mud-input-control-margin-dense',
  'mud-input-control-margin-normal', 'mud-input-helper-onfocus', 'mud-input-helper-text-contained', 'mud-input-helper-text-margin-dense',
  'mud-input-input-control', 'mud-input-label-animated', 'mud-input-label-filled', 'mud-input-label-inputcontrol',
  'mud-input-label-margin-dense', 'mud-input-label-margindense', 'mud-input-label-outlined', 'mud-input-label-shrink',
  'mud-input-number-control', 'mud-input-numeric-spin', 'mud-input-outlined-border', 'mud-input-outlined-with-label',
  'mud-input-root-adorned-end', 'mud-input-root-adorned-start', 'mud-input-root-filled-shrink', 'mud-input-root-hidden-label',
  'mud-input-root-margin-dense', 'mud-input-root-multiline', 'mud-input-root-outlined', 'mud-input-root-type-search',
  'mud-input-slot-nopadding', 'mud-input-text-with-label', 'mud-input-with-content', 'mud-link-underline-always',
  'mud-link-underline-hover', 'mud-link-underline-none', 'mud-list-item-clickable', 'mud-list-item-disabled',
  'mud-list-item-secondary-text', 'mud-list-item-text-inset', 'mud-list-subheader-gutters', 'mud-list-subheader-inset',
  'mud-list-subheader-sticky', 'mud-menu-button-hidden', 'mud-nav-group-disabled', 'mud-nav-link-expand-icon', 'mud-nav-link-icon-default',
  'mud-navmenu-margin-dense', 'mud-navmenu-margin-normal', 'mud-pagination-disable-elevation', 'mud-pagination-item-rectangular',
  'mud-pagination-item-selected', 'mud-pagination-outlined', 'mud-panel-next-expanded', 'mud-picker-calendar-container',
  'mud-picker-calendar-content', 'mud-picker-calendar-content-1', 'mud-picker-calendar-content-2', 'mud-picker-calendar-header-1',
  'mud-picker-calendar-header-10', 'mud-picker-calendar-header-11', 'mud-picker-calendar-header-12', 'mud-picker-calendar-header-2',
  'mud-picker-calendar-header-3', 'mud-picker-calendar-header-4', 'mud-picker-calendar-header-5', 'mud-picker-calendar-header-6',
  'mud-picker-calendar-header-7', 'mud-picker-calendar-header-8', 'mud-picker-calendar-header-9', 'mud-picker-calendar-header-day',
  'mud-picker-calendar-header-last', 'mud-picker-calendar-header-switch', 'mud-picker-calendar-header-transition',
  'mud-picker-calendar-progress-container', 'mud-picker-calendar-transition', 'mud-picker-calendar-week', 'mud-picker-calendar-week-text',
  'mud-picker-color-collection', 'mud-picker-color-content', 'mud-picker-color-controls', 'mud-picker-color-controls-row',
  'mud-picker-color-dot-current', 'mud-picker-color-inputfield', 'mud-picker-color-inputs', 'mud-picker-color-overlay',
  'mud-picker-color-overlay-black', 'mud-picker-color-overlay-white', 'mud-picker-color-picker', 'mud-picker-color-selector',
  'mud-picker-color-slider', 'mud-picker-color-sliders', 'mud-picker-color-toolbar', 'mud-picker-color-view-collection',
  'mud-picker-container-landscape', 'mud-picker-content-landscape', 'mud-picker-control-switch', 'mud-picker-datepicker-date-landscape',
  'mud-picker-datepicker-toolbar', 'mud-picker-datepicker-toolbar-landscape', 'mud-picker-inline-paper', 'mud-picker-input-button',
  'mud-picker-month-container', 'mud-picker-month-selected', 'mud-picker-nav-button-next', 'mud-picker-nav-button-prev',
  'mud-picker-popover-paper', 'mud-picker-slide-transition', 'mud-picker-stick-inner', 'mud-picker-stick-outer',
  'mud-picker-time-clock-mask', 'mud-picker-time-clock-pin', 'mud-picker-time-clock-pointer', 'mud-picker-time-clock-pointer-animation',
  'mud-picker-time-clock-pointer-thumb', 'mud-picker-time-container', 'mud-picker-timepicker-toolbar',
  'mud-picker-timepicker-toolbar-landscape', 'mud-picker-toolbar-landscape', 'mud-picker-year-container', 'mud-picker-year-selected',
  'mud-popover-cascading-value', 'mud-popover-relative-width', 'mud-progress-circular-circle', 'mud-progress-circular-circle-rounded',
  'mud-progress-circular-disable-shrink', 'mud-progress-circular-indeterminate-child', 'mud-progress-circular-percentage',
  'mud-progress-circular-svg', 'mud-progress-indeterminate', 'mud-progress-linear-1-indeterminate', 'mud-progress-linear-2-indeterminate',
  'mud-progress-linear-background', 'mud-progress-linear-bar', 'mud-progress-linear-bars', 'mud-progress-linear-buffer',
  'mud-progress-linear-color-dark', 'mud-progress-linear-color-default', 'mud-progress-linear-color-error',
  'mud-progress-linear-color-info', 'mud-progress-linear-color-primary', 'mud-progress-linear-color-secondary',
  'mud-progress-linear-color-success', 'mud-progress-linear-color-tertiary', 'mud-progress-linear-color-warning',
  'mud-progress-linear-content', 'mud-progress-linear-dashed', 'mud-progress-linear-large', 'mud-progress-linear-medium',
  'mud-progress-linear-rounded', 'mud-progress-linear-small', 'mud-progress-linear-striped', 'mud-radio-icon-checked',
  'mud-range-end-selected', 'mud-range-input-separator', 'mud-range-selection-dark', 'mud-range-selection-error',
  'mud-range-selection-info', 'mud-range-selection-primary', 'mud-range-selection-secondary', 'mud-range-selection-success',
  'mud-range-selection-tertiary', 'mud-range-selection-warning', 'mud-range-start-selected', 'mud-rating-item-active',
  'mud-ripple-effect-expanding', 'mud-ripple-effect-fading', 'mud-row-group-indented-2', 'mud-row-group-indented-3',
  'mud-row-group-indented-4', 'mud-row-group-indented-5', 'mud-slider-inner-container', 'mud-slider-track-tick-label',
  'mud-slider-value-label', 'mud-snackbar-content-action', 'mud-snackbar-content-message', 'mud-snackbar-location-bottom-center',
  'mud-snackbar-location-bottom-left', 'mud-snackbar-location-bottom-right', 'mud-snackbar-location-top-center',
  'mud-snackbar-location-top-left', 'mud-snackbar-location-top-right', 'mud-step-label-content', 'mud-step-label-content-title',
  'mud-stepper-nav-connector', 'mud-stepper-nav-connector-line', 'mud-stepper-nav-scrollable', 'mud-stepper-step-button',
  'mud-stepper__center-labels', 'mud-stepper__horizontal', 'mud-switch-base-medium', 'mud-switch-span-medium', 'mud-switch-thumb-large',
  'mud-switch-thumb-medium', 'mud-switch-thumb-small', 'mud-tab-slider-horizontal', 'mud-tab-slider-horizontal-reverse',
  'mud-tab-slider-vertical', 'mud-tab-slider-vertical-reverse', 'mud-table-body-virtualized', 'mud-table-cell-align-center',
  'mud-table-cell-align-justify', 'mud-table-cell-align-left', 'mud-table-cell-align-right', 'mud-table-cell-checkbox',
  'mud-table-child-content', 'mud-table-loading-progress', 'mud-table-page-number-information', 'mud-table-pagination-actions',
  'mud-table-pagination-caption', 'mud-table-pagination-display', 'mud-table-pagination-information', 'mud-table-pagination-select',
  'mud-table-pagination-spacer', 'mud-table-pagination-toolbar', 'mud-table-row-clickable', 'mud-table-row-disabled',
  'mud-table-row-expander', 'mud-table-row-group-indented-1', 'mud-table-row-group-indented-2', 'mud-table-row-group-indented-3',
  'mud-table-row-group-indented-4', 'mud-table-row-group-indented-5', 'mud-table-small-alignright', 'mud-table-smalldevices-sortselect',
  'mud-table-sort-label-icon', 'mud-table-sticky-footer', 'mud-table-sticky-header', 'mud-tabs-border-bottom',
  'mud-tabs-dropzone-horizontal', 'mud-tabs-dropzone-vertical', 'mud-tabs-header-before', 'mud-tabs-panel-header-after',
  'mud-tabs-panel-header-before', 'mud-tabs-scroll-button', 'mud-tabs-tabbar-content', 'mud-tabs-tabbar-primary',
  'mud-tabs-tabbar-secondary', 'mud-tabs-tabbar-success', 'mud-tabs-tabbar-tertiary', 'mud-tabs-tabbar-warning', 'mud-tabs-tabbar-wrapper',
  'mud-tabs-vertical-reverse', 'mud-time-picker-dial-hidden', 'mud-time-picker-dial-out', 'mud-time-picker-minute',
  'mud-timeline-align-end', 'mud-timeline-align-start', 'mud-timeline-dot-default', 'mud-timeline-dot-error', 'mud-timeline-dot-primary',
  'mud-timeline-dot-secondary', 'mud-timeline-dot-size-large', 'mud-timeline-dot-size-medium', 'mud-timeline-dot-size-small',
  'mud-timeline-dot-success', 'mud-timeline-dot-tertiary', 'mud-timeline-dot-warning', 'mud-timeline-horizontal',
  'mud-timeline-item-content', 'mud-timeline-item-divider', 'mud-timeline-item-dot-inner', 'mud-timeline-item-opposite',
  'mud-timeline-item-start', 'mud-timeline-modifiers', 'mud-timeline-position-alternate', 'mud-timeline-position-bottom',
  'mud-timeline-position-end', 'mud-timeline-position-start', 'mud-timeline-position-top', 'mud-timepicker-hourminute',
  'mud-timepicker-separator', 'mud-timepicker-toolbar-text', 'mud-toggle-group-error', 'mud-toggle-group-horizontal',
  'mud-toggle-group-outlined', 'mud-toggle-group-primary', 'mud-toggle-group-secondary', 'mud-toggle-group-success',
  'mud-toggle-group-tertiary', 'mud-toggle-group-vertical', 'mud-toggle-group-warning', 'mud-toggle-item-check-icon',
  'mud-toggle-item-content', 'mud-toggle-item-delimiter', 'mud-toggle-item-size-large', 'mud-toggle-item-size-small',
  'mud-toolbar-wrap-content', 'mud-tooltip-bottom-center', 'mud-tooltip-center-left', 'mud-tooltip-center-right', 'mud-tooltip-top-center',
  'mud-transform-disabled', 'mud-treeview-checked-dark', 'mud-treeview-checked-error', 'mud-treeview-checked-info',
  'mud-treeview-checked-primary', 'mud-treeview-checked-secondary', 'mud-treeview-checked-success', 'mud-treeview-checked-tertiary',
  'mud-treeview-checked-warning', 'mud-treeview-item-arrow', 'mud-treeview-item-arrow-expand', 'mud-treeview-item-arrow-load',
  'mud-treeview-item-checkbox', 'mud-treeview-item-content', 'mud-treeview-item-disabled', 'mud-treeview-item-icon',
  'mud-treeview-item-label', 'mud-treeview-item-selected', 'mud-treeview-select-none', 'mud-treeview-selected-dark',
  'mud-treeview-selected-error', 'mud-treeview-selected-info', 'mud-treeview-selected-primary', 'mud-treeview-selected-secondary',
  'mud-treeview-selected-success', 'mud-treeview-selected-tertiary', 'mud-treeview-selected-warning', 'mud-typography-align-center',
  'mud-typography-align-justify', 'mud-typography-align-left', 'mud-typography-align-right', 'mud-typography-caption',
  'mud-typography-gutterbottom', 'mud-typography-overline', 'mud-typography-paragraph', 'mud-typography-subtitle1',
  'mud-typography-subtitle2',
];

function loadAllow() {
  const allow = new Set(FIXED_ALLOW);
  for (const s of MUD_CLASS_ALLOW) allow.add(s);
  // 판독 선택자 이름 등 공개 고정 이름: rules.json 의 mask_allow (문자열 배열 · 16진은 받지 않음)
  const rules = path.join(__dirname, 'rules.json');
  if (fs.existsSync(rules)) {
    const j = JSON.parse(fs.readFileSync(rules, 'utf8'));
    if (Array.isArray(j.mask_allow)) {
      for (const s of j.mask_allow) if (typeof s === 'string' && !HEX_RE.test(s)) allow.add(s);
    }
  }
  return allow;
}

function knownHex(env) {
  const set = new Set(KNOWN_HEX);
  const sums = path.join(__dirname, 'expected-sums.txt');
  if (fs.existsSync(sums)) {
    for (const l of fs.readFileSync(sums, 'utf8').split('\n')) {
      const m = /^([0-9a-f]{64})\s/.exec(l);
      if (m) set.add(m[1]);
    }
  }
  if (env && typeof env.GITHUB_SHA === 'string' && /^[0-9a-f]{40}$/.test(env.GITHUB_SHA)) set.add(env.GITHUB_SHA);
  // 고침5 H2-2(결-7): 판독 규칙 · 판독기 파일 sha256(정확 일치 · 공개 파일의 지문) — run.json rules_sha256 · judge_sha256 · verdict rulesSha256 가 가림에 지워지지 않게
  for (const s of ruleFileShas()) set.add(s);
  return set;
}

// 고침5 H2-2: 이 나무 rules.json · judge.js 바이트의 sha256(파일 없으면 뺌) — boot.sh runjson 이 같은 방법으로 셈
const RULE_FILES = ['rules.json', 'judge.js'];
function ruleFileShas() {
  const out = [];
  for (const n of RULE_FILES) {
    const p = path.join(__dirname, n);
    if (fs.existsSync(p)) out.push(crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex'));
  }
  return out;
}

// fileBase: 파일 이름(SHA256SUMS.raw 줄머리 판단용) · 이름 검사 때는 null
function hexCtx(env, fileBase) {
  const deviceFile = typeof fileBase === 'string' && (DEVICE_HEX_FILES.has(fileBase) || fileBase.startsWith('m1-'));
  return { known: knownHex(env), sumsFile: fileBase === 'SHA256SUMS.raw', deviceFile };
}

function hexAllowedAt(text, idx, tok, hctx) {
  if (hctx.known.has(tok.toLowerCase())) return true;
  if (!hctx.deviceFile) return false;
  const pre = text.slice(Math.max(0, idx - 64), idx);
  let m = /"([A-Za-z0-9_]+)"\s*:\s*"$/.exec(pre);
  if (m && HEX_KEYS.has(m[1])) return true;
  m = /(?:^|[^A-Za-z0-9_])([A-Za-z0-9_]+)=$/.exec(pre);
  if (m && HEX_KV.has(m[1])) return true;
  if (/armed:$/.test(pre)) return true;
  if (hctx.sumsFile && (idx === 0 || text[idx - 1] === '\n') && text.startsWith('  ', idx + tok.length)) return true;
  return false;
}

function pathLike(body) {
  const segs = body.split('/');
  return segs.length >= 2 && segs.every((x) => x.length >= 2 && PATH_SEG_RE.test(x));
}

// 고침2 Y2-b: 길이 >= 22 · 대 · 소 둘 다 · (숫자 또는 몸 안 + / 또는 끝 = 패딩) — '/' 만으로 자격이면 낱말 경로 꼴은 뺀다
function isB64Secret(tok) {
  const body = tok.replace(/=+$/, '');
  const padded = body.length !== tok.length;
  if (body.length < 22) return false;
  if (!/[+/]/.test(body) && !padded) return false; // 순수 영숫자는 TOKEN_RE 몫
  if (!/[A-Z]/.test(body) || !/[a-z]/.test(body)) return false;
  if (/[0-9]/.test(body) || body.includes('+') || padded) return true;
  return !pathLike(body);
}

// 가림 · 누출 공용 — 걸리는 덩어리 [{s, e, tok, kind}] (앞에서부터)
//   고침4 G7: kind(꼴 종류 · 가림 기록용 · 값 아님) 를 더함 — base64 · hex · long-token
function findSecrets(text, allow, hctx) {
  const out = [];
  for (const m of text.matchAll(B64_RE)) {
    if (isB64Secret(m[0]) && !allow.has(m[0])) out.push({ s: m.index, e: m.index + m[0].length, tok: m[0], kind: 'base64' });
  }
  for (const m of text.matchAll(TOKEN_RE)) {
    const s = m.index;
    const e = s + m[0].length;
    if (out.some((o) => s < o.e && e > o.s)) continue;
    const tok = m[0];
    if (allow.has(tok)) continue;
    if (HEX_RE.test(tok) && hexAllowedAt(text, s, tok, hctx)) continue;
    out.push({ s, e, tok, kind: HEX_RE.test(tok) ? 'hex' : 'long-token' });
  }
  return out.sort((a, b) => a.s - b.s);
}

// ── 고침4 G7 — 판정 칸 스키마(rules.json judgeSchema) · 칸 경로 ──────────────────────
//   꼴 글자: 'kbd.path[].tag' · 'items.*.labels[]' — 점 = 객체 칸 · [] = 배열 아무 칸 · * = 아무 열쇠
const IDX = { idx: true };
const ANY = { any: true };
function parsePattern(p) {
  const out = [];
  for (const seg of String(p).split('.')) {
    let name = seg;
    let n = 0;
    while (name.endsWith('[]')) { name = name.slice(0, -2); n += 1; }
    out.push(name === '*' ? ANY : name);
    for (let i = 0; i < n; i++) out.push(IDX);
  }
  return out;
}
function matchPath(pathArr, pat) {
  if (pathArr.length !== pat.length) return false;
  return pat.every((t, i) => (t === IDX ? typeof pathArr[i] === 'number' : t === ANY ? typeof pathArr[i] === 'string' : pathArr[i] === t));
}
function inSchema(pathArr, pats) { return pats.some((p) => matchPath(pathArr, p)); }
function schemaFor(rules, fileBase) {
  const s = rules && rules.judgeSchema;
  const list = s && Array.isArray(s[fileBase]) ? s[fileBase] : [];
  return list.map(parsePattern);
}
// 칸 경로 글자(가림 기록용) — 열쇠가 긴 토큰 꼴이면 열쇠도 <키> 로(값 0)
const KEY_LONG = /[A-Za-z0-9_+\/=-]{22,}/;
function pathStr(pathArr) {
  let o = '';
  for (const k of pathArr) {
    if (typeof k === 'number') o += '[' + k + ']';
    else o += (o ? '.' : '') + (KEY_LONG.test(k) ? '<키>' : k);
  }
  return o || '(뿌리)';
}
// 객체에서 스키마 꼴에 맞는 [경로 배열, 값] 들(judge.js 가 표본 하나에 건다)
function schemaValues(obj, pats) {
  const out = [];
  const walk = (v, p) => {
    if (inSchema(p, pats)) out.push([p.slice(), v]);
    if (v && typeof v === 'object') {
      if (Array.isArray(v)) v.forEach((x, i) => walk(x, p.concat([i])));
      else for (const k of Object.keys(v)) walk(v[k], p.concat([k]));
    }
  };
  walk(obj, []);
  return out;
}
// JSON · JSONL 원문의 글자 덩어리(따옴표 포함) 자리 → 칸 경로 [{s, e, path, key}] · jsonl 은 줄(line)도
function jsonSpans(text) {
  const spans = [];
  const stack = [];
  let line = 1;
  const cur = () => stack.map((f) => (f.type === 'a' ? f.idx : f.key));
  let i = 0;
  while (i < text.length) {
    const ch = text[i];
    if (ch === '"') {
      let j = i + 1;
      while (j < text.length && text[j] !== '"') { if (text[j] === '\\') j++; j++; }
      const top = stack[stack.length - 1];
      if (top && top.type === 'o' && top.expectKey) {
        let key = '';
        try { key = JSON.parse(text.slice(i, j + 1)); } catch (e) { key = '<키>'; }
        spans.push({ s: i, e: j + 1, path: cur().slice(0, -1).concat([key]), key: true, line });
        top.key = key;
        top.expectKey = false;
      } else {
        spans.push({ s: i, e: j + 1, path: cur(), key: false, line });
      }
      i = j + 1;
      continue;
    }
    if (ch === '{') stack.push({ type: 'o', key: null, expectKey: true });
    else if (ch === '[') stack.push({ type: 'a', idx: 0 });
    else if (ch === '}' || ch === ']') stack.pop();
    else if (ch === ',') { const top = stack[stack.length - 1]; if (top) { if (top.type === 'a') top.idx += 1; else top.expectKey = true; } }
    else if (ch === '\n') line += 1;
    i++;
  }
  return spans;
}

// 옛 호출 꼴(문맥 없는 낱말 판정) — 16진 40/64 는 문맥이 없으니 걸린다
function isLongToken(tok, allow) {
  return tok.length >= 22 && !allow.has(tok);
}

function runnerPaths(env) {
  const vals = ['GITHUB_WORKSPACE', 'RUNNER_TEMP', 'HOME', 'RUNNER_TOOL_CACHE', 'RUNNER_WORKSPACE']
    .map((k) => env[k])
    .filter((v) => typeof v === 'string' && v.length >= 5 && v !== '/');
  return [...new Set(vals)].sort((a, b) => b.length - a.length);
}

function listFiles(dir) {
  const out = [];
  const walk = (d) => {
    for (const ent of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, ent.name);
      if (ent.isDirectory()) walk(p);
      else out.push(p);
    }
  };
  walk(dir);
  return out.sort();
}

// 고침4 G7: opt = { file(상대 경로) · json(참이면 칸 경로) · schema(판정 칸 꼴) } — 없으면 옛 꼴 그대로
//   판정 칸에 걸린 덩어리는 가리지 않고 counts.conflicts 에(값 0) · 그 밖은 가리고 counts.records 에(파일 · 칸 경로 · 꼴 · 길이)
const RECORDS_MAX = 500;
function maskText(text, paths, allow, tokenIndex, counts, hctx, opt) {
  const o = opt || {};
  let t = text;
  for (const p of paths) {
    const parts = t.split(p);
    if (parts.length > 1) { counts.paths += parts.length - 1; t = parts.join('<러너>'); }
  }
  const all = findSecrets(t, allow, hctx);
  const spans = o.json ? jsonSpans(t) : null;
  const where = (h) => {
    if (spans) {
      const sp = spans.find((x) => h.s >= x.s && h.e <= x.e);
      if (sp) return { arr: sp.key ? null : sp.path, str: (o.jsonl ? 'L' + sp.line + ':' : '') + pathStr(sp.path) + (sp.key ? '(열쇠)' : '') };
    }
    const line = t.slice(0, h.s).split('\n').length;
    return { arr: null, str: 'L' + line };
  };
  const hits = [];
  for (const h of all) {
    const w = where(h);
    const rec = { file: o.file || null, path: w.str, kind: h.kind, length: h.tok.length };
    if (o.schema && o.schema.length && w.arr && inSchema(w.arr, o.schema)) {
      if (counts.conflicts) counts.conflicts.push(rec);
      continue;
    }
    if (counts.records) { if (counts.records.length < RECORDS_MAX) counts.records.push(rec); else counts.records_dropped = (counts.records_dropped || 0) + 1; }
    hits.push(h);
  }
  // 고침5 H2-3: counts.indexBase = 앞 회차가 쓴 번호 수(--only 회차는 그 다음부터) · 없으면 0(옛 꼴 그대로)
  for (const h of hits) if (!tokenIndex.has(h.tok)) tokenIndex.set(h.tok, (counts.indexBase || 0) + tokenIndex.size + 1);
  for (let i = hits.length - 1; i >= 0; i--) {
    const h = hits[i];
    counts.tokens += 1;
    t = t.slice(0, h.s) + '<가림:' + tokenIndex.get(h.tok) + '>' + t.slice(h.e);
  }
  return t;
}

function loadRulesSafe() {
  const p = path.join(__dirname, 'rules.json');
  try { return fs.existsSync(p) ? JSON.parse(fs.readFileSync(p, 'utf8')) : null; } catch (e) { return null; }
}

// 고침5 H2-3: opts = { only(원자료 폴더 안 상대 경로 · 그 파일만) · summaryKey(run.json 칸 · only 면 기본 mask_verdict) } — 없으면 옛 꼴 그대로
function onlyTarget(dir, only) {
  const root = path.resolve(dir);
  const p = path.resolve(root, String(only));
  if (p === root || !p.startsWith(root + path.sep)) throw new Error('--only 경로가 원자료 폴더 밖');
  return p;
}
function maskDir(dir, env, opts) {
  const o = opts || {};
  const summaryKey = o.summaryKey || (o.only ? 'mask_verdict' : 'mask');
  if (o.only && summaryKey === 'mask') throw new Error('--only 회차는 옛 mask 칸에 쓰지 않는다(덮어쓰기 0)');
  const runJson = path.join(dir, 'run.json');
  const prevRun = o.only && fs.existsSync(runJson) ? JSON.parse(fs.readFileSync(runJson, 'utf8')) : null;
  const indexBase = prevRun && prevRun.mask && Number.isInteger(prevRun.mask.distinct_tokens) ? prevRun.mask.distinct_tokens : 0;
  const targets = o.only ? [onlyTarget(dir, o.only)].filter((p) => fs.existsSync(p)) : listFiles(dir);
  const allow = loadAllow();
  const paths = runnerPaths(env);
  const tokenIndex = new Map();
  const rules = loadRulesSafe();
  const counts = { files: 0, paths: 0, tokens: 0, json_broken: [], records: [], conflicts: [], indexBase };
  for (const f of targets) {
    if (!TEXT_EXT.has(path.extname(f))) continue;
    const before = fs.readFileSync(f, 'utf8');
    const base = path.basename(f);
    const ext = path.extname(f);
    // 가림 기록의 파일 이름도 값 0 — 이름 조각이 가림 꼴에 걸리면 <이름> 으로
    const rel = path.relative(dir, f).split(path.sep).join('/').split('/').map((x) => (findSecrets(x, allow, hexCtx(env, null)).length ? '<이름>' : x)).join('/');
    const opt = { file: rel, json: ext === '.json' || ext === '.jsonl', jsonl: ext === '.jsonl', schema: schemaFor(rules, base) };
    const after = maskText(before, paths, allow, tokenIndex, counts, hexCtx(env, base), opt);
    counts.files += 1;
    if (after !== before) {
      fs.writeFileSync(f, after);
      const rel = path.relative(dir, f);
      if (f.endsWith('.json')) {
        try { JSON.parse(after); } catch (e) { counts.json_broken.push({ file: rel, error: String(e.message).slice(0, 120) }); }
      } else if (f.endsWith('.jsonl')) {
        after.split('\n').forEach((l, i) => {
          if (!l.trim()) return;
          try { JSON.parse(l); } catch (e) { counts.json_broken.push({ file: rel, line: i + 1, error: String(e.message).slice(0, 120) }); }
        });
      }
    }
  }
  // 고침4 G7: 가림 기록(파일 · 칸 경로 · 꼴 · 길이 · 값 0) · 판정 칸 가림 충돌(가리지 않음 → leak-scan 이 그 파일 보류)
  const summary = {
    files: counts.files, paths_replaced: counts.paths, tokens_replaced: counts.tokens, distinct_tokens: tokenIndex.size, json_broken: counts.json_broken,
    records: counts.records, records_dropped: counts.records_dropped || 0, judge_field_conflicts: counts.conflicts
  };
  if (o.only) { summary.only = o.only; summary.only_present = targets.length === 1; summary.index_base = indexBase; }
  if (fs.existsSync(runJson)) {
    const j = JSON.parse(fs.readFileSync(runJson, 'utf8'));
    j[summaryKey] = summary;
    fs.writeFileSync(runJson, JSON.stringify(j, null, 2) + '\n');
  }
  tokenIndex.clear();
  return summary;
}

module.exports = {
  TEXT_EXT, TOKEN_RE, B64_RE, HEX_RE, HEX_KEYS, HEX_KV, KNOWN_HEX, FIXED_ALLOW, DEVICE_HEX_FILES, pathLike,
  MUD_CLASS_ALLOW, MUD_CLASS_ALLOW_SHA256, ruleFileShas, RULE_FILES, onlyTarget,
  loadAllow, knownHex, hexCtx, hexAllowedAt, isB64Secret, findSecrets, runnerPaths, isLongToken, listFiles, maskText, maskDir,
  parsePattern, matchPath, inSchema, schemaFor, schemaValues, pathStr, jsonSpans,
};

if (require.main === module) {
  const dir = process.argv[2];
  if (!dir || !fs.existsSync(dir)) { console.error('usage: node mask.js <raw-dir> [--only <rel> [--summary-key <key>]]'); process.exit(1); }
  const argOf = (n) => { const i = process.argv.indexOf(n); return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : null; };
  try {
    const only = argOf('--only');
    const s = maskDir(dir, process.env, { only, summaryKey: argOf('--summary-key') });
    console.log((only ? '[M8′] 가림 끝(그 파일만) ' : '[M8] 가림 끝 ') + JSON.stringify(s));
    process.exit(0);
  } catch (e) {
    console.error('[M8] 가림 오류: ' + String(e && e.message ? e.message : e).slice(0, 300));
    process.exit(1);
  }
}
