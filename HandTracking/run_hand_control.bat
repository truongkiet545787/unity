@echo off
title PoseKart - MediaPipe Hand Gesture Controller
color 0A
echo ===================================================================
echo     POSEKART: HE THONG DIEU KHIEN VO LANG AO (HAND TRACKING)
echo ===================================================================
echo.
cd /d "%~dp0"

echo [1/3] Kiem tra moi truong Python...
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [LOI] Khong tim thay Python! Vui long cai dat Python 3.10 hoac 3.11 va them vao PATH.
    pause
    exit /b 1
)

echo [2/3] Kiem tra thu vien can thiet...
python -c "import cv2, mediapipe, numpy" >nul 2>&1
if %errorlevel% neq 0 (
    echo [THONG BAO] Dang cai dat cac thu vien con thieu tu requirements.txt...
    python -m pip install -r requirements.txt
)

echo [3/3] Khoi dong chuong trinh dieu khien ban tay...
echo.
echo ===================================================================
echo   - Cong UDP gui toi Unity: 127.0.0.1:5052
echo   - Nhan [C] tren cua so camera de HIEU CHUAN goc trung hoa
echo   - Nhan [R] de Reset hieu chuan ve 0 do
echo   - Nhan [I] de Dao chieu danh lai (Invert Steering)
echo   - Nhan [Q] hoac [ESC] de Thoat
echo ===================================================================
echo.

python hand_control.py

if %errorlevel% neq 0 (
    echo.
    echo Chuong trinh ket thuc voi ma loi %errorlevel%.
    pause
)
